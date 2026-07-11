using System.Net;
using System.Security.Claims;
using IdentityCenter.API.Authentication;
using IdentityCenter.API.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IdentityCenter.Tests;

/// <summary>
/// Day 5 debt 2: the anonymous rate-limiter key (and every other client-IP reader) must be
/// trust-aware. A spoofed X-Forwarded-For from an untrusted remote collapses to the socket
/// address — attackers can no longer mint unlimited fresh anonymous identities; behind a
/// configured trusted proxy the forwarded client IP is honored as before.
/// (Fork mirror of main's RateLimitClientKeyTests; ClientIp lives in
/// IdentityCenter.API.Authentication here, not DataAccessLibrary.Security.)
/// </summary>
public class RateLimitClientKeyTests
{
    private static HttpContext BuildContext(string? remoteIp, string? xff = null, string? nameIdentifier = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteIp is null ? null : IPAddress.Parse(remoteIp);
        if (xff is not null)
            context.Request.Headers["X-Forwarded-For"] = xff;
        if (nameIdentifier is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, nameIdentifier) }, "test"));
        }
        return context;
    }

    private static IConfiguration BuildConfig(params string[] trustedProxies)
    {
        var values = new Dictionary<string, string?>();
        for (var i = 0; i < trustedProxies.Length; i++)
            values[$"Api:TrustedProxies:{i}"] = trustedProxies[i];
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Theory]
    [InlineData("1.2.3.4")]
    [InlineData("9.9.9.9, 8.8.8.8")]
    public void Anonymous_SpoofedXff_UntrustedRemote_KeysOnSocketAddress(string spoofed)
    {
        var context = BuildContext("203.0.113.50", xff: spoofed);
        Assert.Equal("ip:203.0.113.50",
            RateLimitingMiddleware.GetClientKey(context, BuildConfig()));
    }

    [Fact]
    public void Anonymous_SpoofedXffIdentities_CollapseToOneKey()
    {
        var config = BuildConfig();
        var a = RateLimitingMiddleware.GetClientKey(BuildContext("203.0.113.50", xff: "1.1.1.1"), config);
        var b = RateLimitingMiddleware.GetClientKey(BuildContext("203.0.113.50", xff: "2.2.2.2"), config);
        var c = RateLimitingMiddleware.GetClientKey(BuildContext("203.0.113.50"), config);
        Assert.Equal(a, b);
        Assert.Equal(a, c); // same connection IP → same sliding window, XFF or not
    }

    [Fact]
    public void Anonymous_XffViaTrustedProxy_KeysOnForwardedClient()
    {
        // The edge-authored shape: the trusted proxy appended the ONE entry (the peer it saw).
        var context = BuildContext("10.0.0.1", xff: "198.51.100.7");
        Assert.Equal("ip:198.51.100.7",
            RateLimitingMiddleware.GetClientKey(context, BuildConfig("10.0.0.1")));
    }

    // ── HIGH-2: rightmost-only XFF resolution behind a trusted proxy ────────────
    // An append-only edge (App Service front end / Front Door / App Gateway) appends the real
    // peer as the RIGHTMOST entry and passes client-supplied entries through on the left. The
    // leftmost token is attacker-chosen; it must NEVER win once Api:TrustedProxies is set.

    [Theory]
    [InlineData("6.6.6.6, 198.51.100.7")]                  // one spoofed hop
    [InlineData("6.6.6.6, 7.7.7.7, 198.51.100.7")]         // many spoofed hops
    [InlineData("203.0.113.99,198.51.100.7")]              // no space after comma
    public void Anonymous_SpoofedLeftXff_TrustedRemote_RightmostEntryWins(string xff)
    {
        var context = BuildContext("10.0.0.1", xff: xff);
        Assert.Equal("ip:198.51.100.7",
            RateLimitingMiddleware.GetClientKey(context, BuildConfig("10.0.0.1")));
    }

    [Fact]
    public void ClientIp_TrustedRemote_MultipleXffHeaderLines_LastLineWins()
    {
        // A client-sent X-Forwarded-For arrives as its own header LINE; an edge that adds a
        // separate line (instead of comma-appending) always adds it after the client's.
        var context = BuildContext("10.0.0.1");
        context.Request.Headers["X-Forwarded-For"] =
            new Microsoft.Extensions.Primitives.StringValues(new[] { "6.6.6.6", "198.51.100.7" });
        Assert.Equal("198.51.100.7", ClientIp.Resolve(context, BuildConfig("10.0.0.1")));
    }

    [Fact]
    public void ClientIp_TrustedRemote_PortSuffixedEntry_ParsesAddress()
    {
        // The App Service front end appends "ip:port".
        var context = BuildContext("10.0.0.1", xff: "6.6.6.6, 198.51.100.7:54321");
        Assert.Equal("198.51.100.7", ClientIp.Resolve(context, BuildConfig("10.0.0.1")));
    }

    [Fact]
    public void ClientIp_TrustedRemote_Ipv4MappedForwardedEntry_Normalized()
    {
        var context = BuildContext("10.0.0.1", xff: "::ffff:198.51.100.7");
        Assert.Equal("198.51.100.7", ClientIp.Resolve(context, BuildConfig("10.0.0.1")));
    }

    [Fact]
    public void ClientIp_UnparseableRightmostEntry_FailsSafeToSocket_LeftwardTokenNeverWins()
    {
        // Fail-safe direction: garbage in the trusted position must NOT promote the (parseable,
        // attacker-chosen) token to its left — collapse to the socket address instead.
        var context = BuildContext("10.0.0.1", xff: "198.51.100.7, garbage");
        Assert.Equal("10.0.0.1", ClientIp.Resolve(context, BuildConfig("10.0.0.1")));
    }

    [Fact]
    public void Anonymous_RemoteNotInTrustedList_XffStillIgnored()
    {
        var context = BuildContext("203.0.113.50", xff: "198.51.100.7");
        Assert.Equal("ip:203.0.113.50",
            RateLimitingMiddleware.GetClientKey(context, BuildConfig("10.0.0.1")));
    }

    [Fact]
    public void Authenticated_KeysOnApiKeyId_XffIrrelevant()
    {
        var context = BuildContext("203.0.113.50", xff: "1.1.1.1", nameIdentifier: "key-guid-123");
        Assert.Equal("key:key-guid-123",
            RateLimitingMiddleware.GetClientKey(context, BuildConfig()));
    }

    [Fact]
    public void Anonymous_NoRemoteAddress_KeysOnUnknown_EvenWithXff()
    {
        // No socket address (test server shape): XFF must STILL not be trusted.
        var context = BuildContext(remoteIp: null, xff: "1.2.3.4");
        Assert.Equal("ip:unknown",
            RateLimitingMiddleware.GetClientKey(context, BuildConfig()));
    }

    [Fact]
    public void ClientIp_Ipv4MappedTrustedProxy_StillMatches()
    {
        var context = BuildContext("::ffff:10.0.0.1", xff: "198.51.100.7");
        Assert.Equal("198.51.100.7", ClientIp.Resolve(context, BuildConfig("10.0.0.1")));
    }

    [Fact]
    public void ClientIp_UnparseableForwardedValue_FallsBackToSocket()
    {
        var context = BuildContext("10.0.0.1", xff: "not-an-ip");
        Assert.Equal("10.0.0.1", ClientIp.Resolve(context, BuildConfig("10.0.0.1")));
    }
}
