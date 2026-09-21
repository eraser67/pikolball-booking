using PickleBallBooking.Services;
using Xunit;

namespace PickleBallBooking.Tests;

/// <summary>
/// Phase 22: unit tests for the pure hostname -> tenant slug parser.
///
/// These pin the security-critical parsing rules with no database or HTTP pipeline:
///  - strict single-label subdomains of the configured base domain resolve;
///  - case-insensitive and port-insensitive;
///  - the base domain, "www", nested subdomains, other domains, missing/malformed
///    hosts and non-DNS-label characters never resolve.
/// </summary>
public class TenantHostParserTests
{
    private const string BaseDomain = "punitbola.com";
    private readonly TenantHostParser _parser = new();

    // -------------------------------------------------------------------------
    // Resolution
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("pikolball.punitbola.com", "pikolball")]
    [InlineData("tenant2.punitbola.com", "tenant2")]
    [InlineData("court-club-99.punitbola.com", "court-club-99")]
    public void TryGetTenantSlug_ResolvesStrictSingleLabelSubdomain(string host, string expected)
    {
        Assert.True(_parser.TryGetTenantSlug(host, BaseDomain, out var slug));
        Assert.Equal(expected, slug);
    }

    [Theory]
    [InlineData("PIKOLBALL.PUNITBOLA.COM")]
    [InlineData("PikolBall.PunitBola.Com")]
    [InlineData("pikolball.PUNITBOLA.com")]
    public void TryGetTenantSlug_IsCaseInsensitive(string host)
    {
        Assert.True(_parser.TryGetTenantSlug(host, BaseDomain, out var slug));
        Assert.Equal("pikolball", slug);
    }

    [Theory]
    [InlineData("pikolball.punitbola.com:5000")]
    [InlineData("pikolball.punitbola.com:80")]
    [InlineData("pikolball.punitbola.com:443")]
    [InlineData("pikolball.punitbola.com:65535")]
    public void TryGetTenantSlug_IgnoresPort(string host)
    {
        Assert.True(_parser.TryGetTenantSlug(host, BaseDomain, out var slug));
        Assert.Equal("pikolball", slug);
    }

    [Fact]
    public void TryGetTenantSlug_ToleratesSingleTrailingDot()
    {
        Assert.True(_parser.TryGetTenantSlug("pikolball.punitbola.com.", BaseDomain, out var slug));
        Assert.Equal("pikolball", slug);
    }

    [Fact]
    public void TryGetTenantSlug_TrimsSurroundingWhitespace()
    {
        Assert.True(_parser.TryGetTenantSlug("  pikolball.punitbola.com  ", BaseDomain, out var slug));
        Assert.Equal("pikolball", slug);
    }

    // -------------------------------------------------------------------------
    // Rejection
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("punitbola.com")]              // base domain itself is not a tenant
    [InlineData("www.punitbola.com")]          // www is not a tenant
    [InlineData("foo.bar.punitbola.com")]      // nested subdomain
    [InlineData("a.b.punitbola.com")]          // nested subdomain
    [InlineData("tenant.otherdomain.com")]     // wrong domain
    [InlineData("pikolball.otherdomain.com")]  // wrong domain
    [InlineData("evilpunitbola.com")]          // suffix without a dot separator
    [InlineData("notpunitbola.com")]           // suffix without a dot separator
    [InlineData("punitbola.com.evil.com")]     // base domain used as a subdomain of another
    [InlineData("pikolball.punitbola.com.evil.com")] // trailing extra label
    [InlineData("")]                            // missing host
    [InlineData("  ")]                          // whitespace host
    [InlineData("localhost")]                   // single label, no base
    [InlineData("-bad.punitbola.com")]          // label starts with hyphen
    [InlineData("bad-.punitbola.com")]          // label ends with hyphen
    [InlineData("has_underscore.punitbola.com")]// invalid DNS label character
    [InlineData("has space.punitbola.com")]     // invalid DNS label character
    [InlineData("teñant.punitbola.com")]        // non-ascii
    [InlineData("[::1]:5000")]                  // IPv6 literal -> not a matching subdomain
    public void TryGetTenantSlug_RejectsNonTenantHosts(string host)
    {
        Assert.False(_parser.TryGetTenantSlug(host, BaseDomain, out var slug));
        Assert.Null(slug);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryGetTenantSlug_Rejects_WhenBaseDomainMissing(string? baseDomain)
    {
        Assert.False(_parser.TryGetTenantSlug("pikolball.punitbola.com", baseDomain, out var slug));
        Assert.Null(slug);
    }

    [Fact]
    public void TryGetTenantSlug_Rejects_WhenHostNull()
    {
        Assert.False(_parser.TryGetTenantSlug(null, BaseDomain, out var slug));
        Assert.Null(slug);
    }

    [Fact]
    public void TryGetTenantSlug_RejectsLongLabelOver63Chars()
    {
        var longLabel = new string('a', 64);
        Assert.False(_parser.TryGetTenantSlug($"{longLabel}.punitbola.com", BaseDomain, out var slug));
        Assert.Null(slug);
    }

    [Fact]
    public void TryGetTenantSlug_BaseDomainComparison_IsCaseInsensitive()
    {
        Assert.True(_parser.TryGetTenantSlug("pikolball.PUNITBOLA.COM", "PunItBola.Com", out var slug));
        Assert.Equal("pikolball", slug);
    }
}
