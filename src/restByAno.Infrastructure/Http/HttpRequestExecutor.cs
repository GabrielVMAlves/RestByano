using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using restByAno.Core.Contracts;
using restByAno.Core.Models;

namespace restByAno.Infrastructure.Http;

/// <summary>
/// Native HTTP transport. 4xx/5xx are valid responses; redirects, cookies, retries and integrated credentials are off;
/// the decoded body is read incrementally against a hard cap; every failure is returned as a structured, secret-free error.
/// </summary>
public sealed class HttpRequestExecutor(HttpClientPool pool, ITargetPolicy targetPolicy) : IRequestExecutor
{
    private const int ReadBufferSize = 16 * 1024;

    // Headers that belong to the content (entity) rather than the request message.
    private static readonly HashSet<string> ContentHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type", "Content-Encoding", "Content-Language", "Content-Location", "Content-MD5",
        "Content-Range", "Content-Disposition", "Expires", "Last-Modified", "Allow",
    };

    // Headers owned by the transport; supplying them would change framing or routing.
    private static readonly HashSet<string> TransportOwnedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Length", "Transfer-Encoding", "Host", "Connection", "Upgrade", "Proxy-Connection", "TE", "Trailer",
    };

    public async Task<ExecutionResult> ExecuteAsync(ResolvedRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invalid = ValidateShape(request);
        if (invalid is not null)
            return Fail(ExecutionErrorKind.Validation, invalid);

        // Destination policy is checked here, immediately before any send, regardless of the caller.
        var decision = targetPolicy.Validate(request.Uri, request.Transport);
        if (!decision.Allowed)
            return Fail(ExecutionErrorKind.PolicyDenied, decision.Reason ?? "The destination is not allowed.");

        if (cancellationToken.IsCancellationRequested)
            return Fail(ExecutionErrorKind.Cancelled, "The request was cancelled.");

        using var message = BuildMessage(request, out var buildError);
        if (message is null)
            return Fail(ExecutionErrorKind.Validation, buildError!);

        using var lease = pool.Acquire(request.Transport);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(request.Transport.TimeoutSeconds));

        var stopwatch = new Stopwatch();
        try
        {
            stopwatch.Start();
            using var response = await lease.Client
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, linked.Token)
                .ConfigureAwait(false);

            var (body, withinLimit) = await ReadBoundedAsync(
                response.Content, request.Transport.MaxResponseBytes, linked.Token).ConfigureAwait(false);
            stopwatch.Stop();

            var snapshot = Snapshot(response, withinLimit ? body : default, stopwatch.ElapsedMilliseconds);
            if (withinLimit)
                return ExecutionResult.FromResponse(snapshot);

            return ExecutionResult.FromError(new ExecutionError(
                ExecutionErrorKind.ResponseLimit,
                $"The response body exceeded the {request.Transport.MaxResponseBytes}-byte limit and was discarded.")
            {
                PartialResponse = snapshot,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            });
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or IOException
                                       or InvalidOperationException or NotSupportedException)
        {
            stopwatch.Stop();
            var error = Classify(ex, cancellationToken.IsCancellationRequested, request.Transport);
            return ExecutionResult.FromError(error with { ElapsedMilliseconds = stopwatch.ElapsedMilliseconds });
        }
    }

    private static string? ValidateShape(ResolvedRequest request)
    {
        var uri = request.Uri;
        if (!uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return "The request URL must be an absolute http or https URL.";
        if (request.Transport.TimeoutSeconds < 1)
            return "The timeout must be at least 1 second.";
        if (request.Transport.MaxResponseBytes < 1)
            return "The response size limit must be positive.";
        if (request.Transport.ProxyMode == ProxyMode.Explicit)
        {
            if (!Uri.TryCreate(request.Transport.ProxyUri, UriKind.Absolute, out var proxy)
                || (proxy.Scheme != Uri.UriSchemeHttp && proxy.Scheme != Uri.UriSchemeHttps))
                return "The configured proxy address must be an absolute http or https URL.";
        }

        return null;
    }

    private static HttpRequestMessage? BuildMessage(ResolvedRequest request, out string? error)
    {
        error = null;
        var message = new HttpRequestMessage(ToHttpMethod(request.Method), request.Uri);

        if (request.HasBody)
        {
            message.Content = new ReadOnlyMemoryContent(request.Body);
        }
        else if (request.ContentType is not null)
        {
            message.Dispose();
            error = "A content type was supplied but the request has no body.";
            return null;
        }

        var contentTypeSet = false;
        if (request.ContentType is not null)
        {
            if (!message.Content!.Headers.TryAddWithoutValidation("Content-Type", request.ContentType))
            {
                message.Dispose();
                error = "The body content type is not valid.";
                return null;
            }

            contentTypeSet = true;
        }

        foreach (var (name, value) in request.Headers)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Any(c => c <= ' ' || c == ':' || c > '~'))
            {
                message.Dispose();
                error = "A header name is empty or contains invalid characters.";
                return null;
            }

            if (value.Contains('\r') || value.Contains('\n'))
            {
                message.Dispose();
                error = $"The value of header '{name}' contains a line break.";
                return null;
            }

            if (TransportOwnedHeaders.Contains(name))
            {
                message.Dispose();
                error = $"Header '{name}' is managed by the transport and cannot be set.";
                return null;
            }

            if (ContentHeaders.Contains(name))
            {
                if (message.Content is null)
                {
                    message.Dispose();
                    error = $"Header '{name}' describes a body, but the request has no body.";
                    return null;
                }

                if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) && contentTypeSet)
                {
                    message.Dispose();
                    error = "Content-Type is specified twice (body type and header).";
                    return null;
                }

                if (!message.Content.Headers.TryAddWithoutValidation(name, value))
                {
                    message.Dispose();
                    error = $"The value of header '{name}' is not valid.";
                    return null;
                }

                contentTypeSet |= name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase);
            }
            else if (!message.Headers.TryAddWithoutValidation(name, value))
            {
                message.Dispose();
                error = $"Header '{name}' or its value is not valid.";
                return null;
            }
        }

        return message;
    }

    private static HttpMethod ToHttpMethod(HttpMethodKind method) => method switch
    {
        HttpMethodKind.Get => HttpMethod.Get,
        HttpMethodKind.Post => HttpMethod.Post,
        HttpMethodKind.Put => HttpMethod.Put,
        HttpMethodKind.Patch => HttpMethod.Patch,
        HttpMethodKind.Delete => HttpMethod.Delete,
        HttpMethodKind.Head => HttpMethod.Head,
        HttpMethodKind.Options => HttpMethod.Options,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
    };

    /// <summary>Reads the decoded stream, at most <paramref name="limit"/> bytes plus a one-byte sentinel.</summary>
    private static async Task<(ReadOnlyMemory<byte> Body, bool WithinLimit)> ReadBoundedAsync(
        HttpContent content, int limit, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[ReadBufferSize];
        long total = 0;
        while (true)
        {
            var wanted = (int)Math.Min(chunk.Length, (long)limit + 1 - total);
            var read = await stream.ReadAsync(chunk.AsMemory(0, wanted), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (total > limit)
                return (default, false); // sentinel byte seen: drop the incomplete body
            buffer.Write(chunk, 0, read);
        }

        return (new ReadOnlyMemory<byte>(buffer.GetBuffer(), 0, (int)buffer.Length), true);
    }

    private static ResponseSnapshot Snapshot(HttpResponseMessage response, ReadOnlyMemory<byte> body, long elapsedMs)
    {
        var headers = new List<HeaderEntry>();
        foreach (var header in response.Headers)
            foreach (var value in header.Value)
                headers.Add(new HeaderEntry(header.Key, value));
        foreach (var header in response.Content.Headers)
            foreach (var value in header.Value)
                headers.Add(new HeaderEntry(header.Key, value));

        return new ResponseSnapshot
        {
            StatusCode = (int)response.StatusCode,
            ReasonPhrase = response.ReasonPhrase,
            Headers = headers,
            Body = body,
            MediaType = response.Content.Headers.ContentType?.MediaType,
            ElapsedMilliseconds = elapsedMs,
        };
    }

    private static ExecutionError Classify(Exception ex, bool userCancelled, TransportProfile transport)
    {
        if (ex is OperationCanceledException)
        {
            return userCancelled
                ? new ExecutionError(ExecutionErrorKind.Cancelled, "The request was cancelled.")
                : new ExecutionError(ExecutionErrorKind.Timeout,
                    $"The request did not complete within {transport.TimeoutSeconds} seconds.");
        }

        if (userCancelled)
            return new ExecutionError(ExecutionErrorKind.Cancelled, "The request was cancelled.");

        var proxyNote = transport.ProxyMode == ProxyMode.Explicit ? " (a proxy is configured)" : string.Empty;

        if (ex is HttpRequestException http)
        {
            // Only report a precise category when .NET exposes one; never guess.
            return http.HttpRequestError switch
            {
                HttpRequestError.NameResolutionError => new ExecutionError(ExecutionErrorKind.DnsOrConnection,
                    "The host name could not be resolved" + proxyNote + "."),
                HttpRequestError.ConnectionError => new ExecutionError(ExecutionErrorKind.DnsOrConnection,
                    "A connection could not be established" + proxyNote + "."),
                HttpRequestError.SecureConnectionError => new ExecutionError(ExecutionErrorKind.Tls,
                    "The TLS handshake or certificate validation failed."),
                HttpRequestError.ProxyTunnelError => new ExecutionError(ExecutionErrorKind.Proxy,
                    "The configured proxy could not establish a tunnel."),
                _ when http.InnerException is SocketException => new ExecutionError(ExecutionErrorKind.DnsOrConnection,
                    "A network error occurred" + proxyNote + "."),
                _ => new ExecutionError(ExecutionErrorKind.OtherTransport,
                    $"The HTTP transport failed ({http.HttpRequestError})."),
            };
        }

        return new ExecutionError(ExecutionErrorKind.OtherTransport, $"The HTTP transport failed ({ex.GetType().Name}).");
    }

    private static ExecutionResult Fail(ExecutionErrorKind kind, string message) =>
        ExecutionResult.FromError(kind, message);
}
