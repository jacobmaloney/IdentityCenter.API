using System.Net;
using Microsoft.Extensions.Configuration;

namespace IdentityCenter.API.Authentication;

/// <summary>
/// Trust-aware client IP resolution. X-Forwarded-For is attacker-controlled on a
/// direct connection, so it is honored ONLY when the remote socket address is in
/// the configured trusted-proxy list (Api:TrustedProxies, an array of IPs).
/// Default (empty list) = always the socket address.
///
/// RIGHTMOST semantics (HIGH-2; the "rightmost" fix DAY5 B4 deferred): when the remote IS a
/// trusted proxy, only the RIGHTMOST entry of the LAST X-Forwarded-For header line is honored.
/// An append-only edge (Azure App Service front end, Front Door, App Gateway) appends the peer
/// it actually saw as the rightmost entry of the header it forwards — every token to its left,
/// and every earlier header line, passed through from the client verbatim and is spoofable.
/// Taking the leftmost entry (pre-2026-07-10) meant a caller who sent its own X-Forwarded-For
/// chose its resolved IP the moment Api:TrustedProxies was populated — fully spoofable rate
/// limiters and audit IPs. An unparseable rightmost token fails SAFE to the socket address; a
/// leftward token is never consulted.
///
/// FORK NOTE: in the IdentityCenter repo this type lives in DataAccessLibrary.Security (shared
/// with WebPortal callers). The fork has no portal, so it stays here — namespace only; the
/// behavior mirrors main's 16c3850d byte-for-byte.
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

        // LAST header line: a client can send its own X-Forwarded-For header; an edge that adds
        // a separate header line (rather than comma-appending) always adds it after the client's.
        var headerLines = context.Request.Headers["X-Forwarded-For"];
        var lastLine = headerLines.Count > 0 ? headerLines[headerLines.Count - 1] : null;
        if (string.IsNullOrEmpty(lastLine))
            return socketAddress;

        // RIGHTMOST token = the one entry the trusted peer itself appended (the address it saw).
        var tokens = lastLine.Split(',');
        var rightmost = tokens[tokens.Length - 1].Trim();

        // The App Service front end appends "ip:port" — parse as an endpoint (the same shape
        // ASP.NET's ForwardedHeadersMiddleware accepts; bare addresses parse with port 0).
        if (!IPEndPoint.TryParse(rightmost, out var forwarded))
            return socketAddress;

        var address = forwarded.Address.IsIPv4MappedToIPv6
            ? forwarded.Address.MapToIPv4()
            : forwarded.Address;
        return address.ToString();
    }
}
