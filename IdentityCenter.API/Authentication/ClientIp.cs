using System.Net;
using Microsoft.Extensions.Configuration;

namespace IdentityCenter.API.Authentication;

/// <summary>
/// Trust-aware client IP resolution. X-Forwarded-For is attacker-controlled on a
/// direct connection, so it is honored ONLY when the remote socket address is in
/// the configured trusted-proxy list (Api:TrustedProxies, an array of IPs).
/// Default (empty list) = always the socket address.
/// </summary>
public static class ClientIp
{
    public static string Resolve(HttpContext context, IConfiguration configuration)
    {
        var remote = context.Connection.RemoteIpAddress;
        var socketAddress = remote?.ToString() ?? "unknown";

        var trustedProxies = configuration.GetSection("Api:TrustedProxies").Get<string[]>();
        if (remote is null || trustedProxies is null || trustedProxies.Length == 0)
            return socketAddress;

        // Normalize IPv4-mapped IPv6 (::ffff:a.b.c.d) on BOTH sides so an IPv4
        // Api:TrustedProxies entry still matches a dual-stack socket address.
        var normalizedRemote = remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote;
        var remoteIsTrusted = trustedProxies.Any(p =>
            IPAddress.TryParse(p, out var proxy)
            && (proxy.IsIPv4MappedToIPv6 ? proxy.MapToIPv4() : proxy).Equals(normalizedRemote));
        if (!remoteIsTrusted)
            return socketAddress;

        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (string.IsNullOrEmpty(forwardedFor))
            return socketAddress;

        var first = forwardedFor.Split(',')[0].Trim();
        return IPAddress.TryParse(first, out var forwarded) ? forwarded.ToString() : socketAddress;
    }
}
