using System.Net;
using restByAno.Core.Contracts;
using restByAno.Core.Models;

namespace restByAno.Infrastructure.Policy;

/// <summary>
/// Destination rule used when no managed target policy is installed: only loopback targets over http/https, with no embedded
/// credentials. <see cref="ManagedTargetPolicy"/> applies it and adds the administrator allow-list. This is an application guard, not a firewall.
/// </summary>
public sealed class LoopbackOnlyTargetPolicy : ITargetPolicy
{
    public TargetPolicyResult Validate(Uri target, TransportProfile transport)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!target.IsAbsoluteUri)
            return TargetPolicyResult.Deny("The request URL must be absolute.");
        if (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps)
            return TargetPolicyResult.Deny("Only http and https URLs are supported.");
        if (!string.IsNullOrEmpty(target.UserInfo))
            return TargetPolicyResult.Deny("URLs with embedded credentials are not allowed.");
        if (!IsLoopbackHost(target))
            return TargetPolicyResult.Deny("Only loopback targets are allowed until a managed target policy is installed.");

        return TargetPolicyResult.Allow;
    }

    internal static bool IsLoopbackHost(Uri target)
    {
        var host = target.Host.TrimStart('[').TrimEnd(']');
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!IPAddress.TryParse(host, out var address))
            return false;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        return IPAddress.IsLoopback(address);
    }
}
