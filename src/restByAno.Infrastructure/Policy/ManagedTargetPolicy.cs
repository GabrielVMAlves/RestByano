using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using restByAno.Core.Contracts;
using restByAno.Core.Models;

namespace restByAno.Infrastructure.Policy;

public enum PolicyMode
{
    /// <summary>No policy file: only loopback destinations are allowed.</summary>
    NotInstalled,

    /// <summary>A valid policy is installed: only listed destinations are allowed (loopback must be listed too).</summary>
    Active,

    /// <summary>A policy file exists but cannot be used: every destination is blocked (fail closed).</summary>
    Invalid,
}

public sealed record AllowedTarget(string Scheme, string Host, int Port);

public sealed record PolicyStatus(PolicyMode Mode, string Path, string Message, IReadOnlyList<AllowedTarget> Targets);

/// <summary>
/// Destination allow-list deployed by an administrator at <c>%PROGRAMDATA%\restByAno\target-policy.json</c>. restByAno only
/// reads it. Matching is exact on scheme, normalized host name and effective port — no wildcards, no suffix matching.
/// This is an application guard, not a replacement for corporate firewall or proxy controls.
/// </summary>
public sealed partial class ManagedTargetPolicy : ITargetPolicy
{
    public const int SupportedSchemaVersion = 1;

    private readonly string _path;
    private readonly object _gate = new();
    private (DateTime LastWrite, long Length)? _cacheKey;
    private PolicyStatus? _cache;

    public ManagedTargetPolicy(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "restByAno", "target-policy.json");
    }

    public string PolicyPath => _path;

    public TargetPolicyResult Validate(Uri target, TransportProfile transport)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!target.IsAbsoluteUri)
            return TargetPolicyResult.Deny("The request URL must be absolute.");
        if (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps)
            return TargetPolicyResult.Deny("Only http and https URLs are supported.");
        if (!string.IsNullOrEmpty(target.UserInfo))
            return TargetPolicyResult.Deny("URLs with embedded credentials are not allowed.");

        var status = GetStatus();
        var host = NormalizeHost(target.IdnHost);
        var loopback = LoopbackOnlyTargetPolicy.IsLoopbackHost(target);

        switch (status.Mode)
        {
            case PolicyMode.Invalid:
                return TargetPolicyResult.Deny(
                    $"The managed target policy cannot be used, so all destinations are blocked: {status.Message} An administrator must fix '{status.Path}'.");

            case PolicyMode.NotInstalled:
                return loopback
                    ? TargetPolicyResult.Allow
                    : TargetPolicyResult.Deny(
                        $"No managed target policy is installed on this device, so only loopback addresses can be used. To reach '{host}' an administrator must deploy a target policy (see the user guide).");
        }

        var scheme = target.Scheme;
        var port = target.Port; // effective port: Uri fills in 80/443
        var match = status.Targets.Any(t => t.Scheme == scheme && t.Host == host && t.Port == port);
        if (!match)
            return TargetPolicyResult.Deny($"“{scheme}://{host}:{port}” is not in the managed allow-list.");

        if (scheme == Uri.UriSchemeHttp && !loopback && !transport.AllowNonLoopbackHttp)
            return TargetPolicyResult.Deny(
                $"“{host}” is allowed by the managed policy, but plain HTTP to a non-loopback host also requires “Allow non-loopback HTTP” in the transport settings of the active environment.");

        return TargetPolicyResult.Allow;
    }

    /// <summary>Reads the policy (cached until the file changes).</summary>
    public PolicyStatus GetStatus()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    _cacheKey = null;
                    _cache = null;
                    return new PolicyStatus(PolicyMode.NotInstalled, _path,
                        "No managed target policy is installed; only loopback destinations are allowed.", []);
                }

                var info = new FileInfo(_path);
                var key = (info.LastWriteTimeUtc, info.Length);
                if (_cache is not null && _cacheKey == key)
                    return _cache;

                var status = Parse(File.ReadAllText(_path));
                _cacheKey = key;
                _cache = status;
                return status;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _cacheKey = null;
                _cache = null;
                return Invalid($"The file could not be read ({ex.GetType().Name}).");
            }
        }
    }

    private PolicyStatus Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Invalid("The policy must be a JSON object.");

            if (!root.TryGetProperty("schemaVersion", out var versionElement) || !versionElement.TryGetInt32(out var version))
                return Invalid("The policy has no numeric schemaVersion.");
            if (version != SupportedSchemaVersion)
                return Invalid($"The policy uses schemaVersion {version}, but this restByAno supports only version {SupportedSchemaVersion}.");

            if (!root.TryGetProperty("allowedTargets", out var list) || list.ValueKind != JsonValueKind.Array)
                return Invalid("The policy has no allowedTargets array.");

            var targets = new List<AllowedTarget>();
            var index = 0;
            foreach (var item in list.EnumerateArray())
            {
                index++;
                var (target, error) = ParseTarget(item);
                if (target is null)
                    return Invalid($"allowedTargets entry {index}: {error}");
                targets.Add(target);
            }

            return new PolicyStatus(PolicyMode.Active, _path, $"{targets.Count} allowed target(s).", targets);
        }
        catch (JsonException)
        {
            return Invalid("The file is not valid JSON.");
        }
    }

    private static (AllowedTarget? Target, string? Error) ParseTarget(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return (null, "must be an object.");
        if (!item.TryGetProperty("scheme", out var schemeElement) || schemeElement.ValueKind != JsonValueKind.String)
            return (null, "needs a string scheme.");
        if (!item.TryGetProperty("host", out var hostElement) || hostElement.ValueKind != JsonValueKind.String)
            return (null, "needs a string host.");

        var scheme = schemeElement.GetString()!.Trim().ToLowerInvariant();
        if (scheme is not ("http" or "https"))
            return (null, "scheme must be http or https.");

        var rawHost = hostElement.GetString()!.Trim();
        if (rawHost.Length == 0 || rawHost.Contains('*') || rawHost.Any(char.IsWhiteSpace) || rawHost.Contains('/') || rawHost.Contains('@') || rawHost.Contains('?'))
            return (null, "host must be one exact host name or IP address (no wildcards, paths or credentials).");

        string host;
        try
        {
            host = NormalizeHost(rawHost);
        }
        catch (ArgumentException)
        {
            return (null, "host is not a valid name.");
        }

        if (!IsValidHost(host))
            return (null, "host is not a valid name.");

        var port = scheme == "https" ? 443 : 80;
        if (item.TryGetProperty("port", out var portElement))
        {
            if (!portElement.TryGetInt32(out port) || port is < 1 or > 65535)
                return (null, "port must be a number from 1 to 65535.");
        }

        return (new AllowedTarget(scheme, host, port), null);
    }

    private static string NormalizeHost(string host)
    {
        var trimmed = host.Trim().TrimStart('[').TrimEnd(']').TrimEnd('.').ToLowerInvariant();
        if (IPAddress.TryParse(trimmed, out var address))
            return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString().ToLowerInvariant();
        return new IdnMapping().GetAscii(trimmed).ToLowerInvariant();
    }

    private static bool IsValidHost(string host) =>
        IPAddress.TryParse(host, out _) || HostNamePattern().IsMatch(host);

    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9_]([a-z0-9_-]{0,61}[a-z0-9_])?)(\.[a-z0-9_]([a-z0-9_-]{0,61}[a-z0-9_])?)*$")]
    private static partial Regex HostNamePattern();

    private PolicyStatus Invalid(string message) => new(PolicyMode.Invalid, _path, message, []);
}
