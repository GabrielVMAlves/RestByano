using System.Net;
using restByAno.Core.Models;

namespace restByAno.Infrastructure.Http;

/// <summary>
/// Bounded set of long-lived <see cref="HttpClient"/> instances keyed by the transport settings that shape the
/// handler (proxy mode and address). Clients are shared across requests and never disposed per request; unused or
/// evicted profiles are retired and disposed once their last in-flight request has released its lease.
/// </summary>
public sealed class HttpClientPool : IDisposable
{
    public const int MaxProfiles = 8;

    private static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(2);
    private static readonly long IdleRetirementMs = (long)TimeSpan.FromMinutes(10).TotalMilliseconds;

    private readonly object _gate = new();
    private readonly Dictionary<PoolKey, Entry> _entries = [];
    private long _sequence;
    private bool _disposed;

    public int ActiveProfileCount
    {
        get
        {
            lock (_gate) return _entries.Count;
        }
    }

    /// <summary>Returns a lease on the client for the profile. Dispose the lease (not the client) when finished.</summary>
    public Lease Acquire(TransportProfile transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var key = new PoolKey(transport.ProxyMode, transport.ProxyMode == ProxyMode.Explicit ? transport.ProxyUri : null);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var now = Environment.TickCount64;

            foreach (var idle in _entries.Where(e => e.Value.Leases == 0 && now - e.Value.LastUsedMs > IdleRetirementMs).ToList())
                Retire(idle.Key, idle.Value);

            if (!_entries.TryGetValue(key, out var entry))
            {
                if (_entries.Count >= MaxProfiles)
                {
                    var oldest = _entries.MinBy(e => e.Value.LastUsedSeq); // strict LRU; tick counts can tie
                    Retire(oldest.Key, oldest.Value);
                }

                entry = new Entry(CreateClient(key));
                _entries[key] = entry;
            }

            entry.Leases++;
            entry.LastUsedMs = now;
            entry.LastUsedSeq = ++_sequence;
            return new Lease(this, entry);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var (key, entry) in _entries.ToList())
                Retire(key, entry);
        }
    }

    private static HttpClient CreateClient(PoolKey key)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            PooledConnectionLifetime = PooledConnectionLifetime,
            // No default/integrated credentials: Credentials and DefaultProxyCredentials stay null.
        };

        if (key.ProxyMode == ProxyMode.Explicit)
        {
            handler.UseProxy = true;
            handler.Proxy = new WebProxy(new Uri(key.ProxyUri!));
        }
        else
        {
            handler.UseProxy = true;
            handler.Proxy = HttpClient.DefaultProxy; // system proxy
        }

        // The per-execution timeout is enforced with a linked CancellationTokenSource, not HttpClient.Timeout.
        return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private void Retire(PoolKey key, Entry entry)
    {
        _entries.Remove(key);
        entry.Retired = true;
        if (entry.Leases == 0) entry.Client.Dispose();
    }

    private void Release(Entry entry)
    {
        lock (_gate)
        {
            entry.Leases--;
            entry.LastUsedMs = Environment.TickCount64;
            if (entry.Retired && entry.Leases == 0) entry.Client.Dispose();
        }
    }

    private readonly record struct PoolKey(ProxyMode ProxyMode, string? ProxyUri);

    internal sealed class Entry(HttpClient client)
    {
        public HttpClient Client { get; } = client;
        public int Leases;
        public long LastUsedMs;
        public long LastUsedSeq;
        public bool Retired;
    }

    public sealed class Lease : IDisposable
    {
        private readonly HttpClientPool _pool;
        private readonly Entry _entry;
        private int _released;

        internal Lease(HttpClientPool pool, Entry entry)
        {
            _pool = pool;
            _entry = entry;
        }

        public HttpClient Client => _entry.Client;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                _pool.Release(_entry);
        }
    }
}
