using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using Xunit;

namespace PickleBallBooking.Tests;

/// <summary>
/// Phase 22: hostname-based tenant resolution security tests.
///
/// These prove:
///  - resolution is driven SOLELY by the request Host header;
///  - client-controlled inputs (query string, route values, form fields, cookies,
///    headers other than Host, a supplied OrganizationId) can NEVER select a tenant;
///  - base domain, www, nested, unknown, wrong-domain, malformed and inactive hosts
///    never resolve a tenant (no Pikolball fallback);
///  - authenticated users only resolve tenants they actually belong to;
///  - anonymous users resolve valid active tenants and get nothing otherwise.
///
/// The EF Core InMemory provider is used because hostname resolution + membership
/// checks are provider-independent. PostgreSQL parity is covered separately.
/// </summary>
public class TenantResolutionSecurityTests
{
    private const string BaseDomain = "punitbola.com";
    private const int PikolballId = 1;
    private const int TenantBId = 2;

    private static string NewDatabase() => Guid.NewGuid().ToString();

    private static async Task<ApplicationDbContext> SeedAsync(
        string database,
        params (int Id, string Slug, OrganizationStatus Status)[] organizations)
    {
        var context = TestDbContextFactory.CreateWithoutTenant(database);
        foreach (var (id, slug, status) in organizations)
        {
            context.Organizations.Add(new Organization { Id = id, Name = slug, Slug = slug, Status = status });
        }

        await context.SaveChangesAsync();
        return context;
    }

    private static TenantResolver CreateResolver(ApplicationDbContext context, HttpContext httpContext)
        => new(
            new HttpContextAccessor { HttpContext = httpContext },
            context,
            new TenantHostParser(),
            Options.Create(new TenantOptions { BaseDomain = BaseDomain }));

    private static HttpContext ContextWithHost(string host)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        return context;
    }

    private static HttpContext AnonymousHttpContext(string host) => ContextWithHost(host);

    private static HttpContext AuthenticatedHttpContext(string host, string userId)
    {
        var context = ContextWithHost(host);
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId) },
            authenticationType: "Test");
        context.User = new ClaimsPrincipal(identity);
        return context;
    }

    // -------------------------------------------------------------------------
    // Resolution (hostname is the selector)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Anonymous_PikolballHost_ResolvesToPikolball()
    {
        var database = NewDatabase();
        await using var context = await SeedAsync(database, (PikolballId, "pikolball", OrganizationStatus.Active));

        var resolver = CreateResolver(context, AnonymousHttpContext("pikolball.punitbola.com"));

        Assert.Equal(PikolballId, await resolver.ResolveOrganizationIdAsync());
    }

    [Fact]
    public async Task Anonymous_Tenant2Host_ResolvesToTenant2()
    {
        var database = NewDatabase();
        await using var context = await SeedAsync(
            database,
            (PikolballId, "pikolball", OrganizationStatus.Active),
            (TenantBId, "tenant2", OrganizationStatus.Active));

        var resolver = CreateResolver(context, AnonymousHttpContext("tenant2.punitbola.com"));

        Assert.Equal(TenantBId, await resolver.ResolveOrganizationIdAsync());
    }

    [Fact]
    public async Task Anonymous_ResolvesRegardlessOfCaseAndPort()
    {
        var database = NewDatabase();
        await using var context = await SeedAsync(database, (PikolballId, "pikolball", OrganizationStatus.Active));

        var resolver = CreateResolver(context, AnonymousHttpContext("PIKOLBALL.PUNITBOLA.COM:5000"));

        Assert.Equal(PikolballId, await resolver.ResolveOrganizationIdAsync());
    }

    // -------------------------------------------------------------------------
    // Rejection (no fallback to Pikolball)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("punitbola.com")]               // base domain
    [InlineData("www.punitbola.com")]           // www
    [InlineData("unknown.punitbola.com")]       // unknown tenant
    [InlineData("foo.bar.punitbola.com")]       // nested subdomain
    [InlineData("tenant.otherdomain.com")]      // wrong domain
    [InlineData("evilpunitbola.com")]           // suffix without dot
    [InlineData("")]                            // missing host
    [InlineData("localhost")]                   // no base domain match
    [InlineData("has_underscore.punitbola.com")]// malformed label
    public async Task Anonymous_InvalidHost_ResolvesToNothing_NoFallback(string host)
    {
        var database = NewDatabase();
        // Pikolball exists and is active; an invalid host must still NOT resolve it.
        await using var context = await SeedAsync(database, (PikolballId, "pikolball", OrganizationStatus.Active));

        var resolver = CreateResolver(context, AnonymousHttpContext(host));

        Assert.Null(await resolver.ResolveOrganizationIdAsync());
    }

    [Fact]
    public async Task Anonymous_InactiveTenant_ResolvesToNothing()
    {
        var database = NewDatabase();
        await using var context = await SeedAsync(database, (TenantBId, "tenant2", OrganizationStatus.Inactive));

        var resolver = CreateResolver(context, AnonymousHttpContext("tenant2.punitbola.com"));

        Assert.Null(await resolver.ResolveOrganizationIdAsync());
    }

    // -------------------------------------------------------------------------
    // Client-controlled input cannot select the tenant
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Tenant_CannotBeSelected_ThroughQueryString()
    {
        var database = NewDatabase();
        await using var context = await SeedAsync(
            database,
            (PikolballId, "pikolball", OrganizationStatus.Active),
            (TenantBId, "tenant2", OrganizationStatus.Active));

        // Host is Pikolball; query string tries to select tenant2 and pass an org id.
        var httpContext = ContextWithHost("pikolball.punitbola.com");
        httpContext.Request.QueryString = new QueryString("?tenant=tenant2&organizationId=2&org=2&slug=tenant2");

        var resolver = CreateResolver(context, httpContext);

        Assert.Equal(PikolballId, await resolver.ResolveOrganizationIdAsync());
    }

    [Fact]
    public async Task Tenant_CannotBeSelected_ThroughRouteValues()
    {
        var database = NewDatabase();
        await using var context = await SeedAsync(
            database,
            (PikolballId, "pikolball", OrganizationStatus.Active),
            (TenantBId, "tenant2", OrganizationStatus.Active));

        var httpContext = ContextWithHost("pikolball.punitbola.com");
        httpContext.Request.RouteValues = new RouteValueDictionary
        {
            ["tenant"] = "tenant2",
            ["slug"] = "tenant2",
            ["organizationId"] = "2"
        };

        var resolver = CreateResolver(context, httpContext);

        Assert.Equal(PikolballId, await resolver.ResolveOrganizationIdAsync());
    }

    [Fact]
    public async Task Tenant_CannotBeSelected_ThroughFormData()
    {
        var database = NewDatabase();
        await using var context = await SeedAsync(
            database,
            (PikolballId, "pikolball", OrganizationStatus.Active),
            (TenantBId, "tenant2", OrganizationStatus.Active));

        var httpContext = ContextWithHost("pikolball.punitbola.com");
        httpContext.Request.ContentType = "application/x-www-form-urlencoded";
        httpContext.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["tenant"] = "tenant2",
            ["organizationId"] = "2",
            ["OrganizationId"] = "2"
        });

        var resolver = CreateResolver(context, httpContext);

        Assert.Equal(PikolballId, await resolver.ResolveOrganizationIdAsync());
    }

    [Fact]
    public async Task Tenant_CannotBeSelected_ThroughCookiesOrHeaders()
    {
        var database = NewDatabase();
        await using var context = await SeedAsync(
            database,
            (PikolballId, "pikolball", OrganizationStatus.Active),
            (TenantBId, "tenant2", OrganizationStatus.Active));

        var httpContext = ContextWithHost("pikolball.punitbola.com");
        httpContext.Request.Headers.Cookie = "tenant=tenant2; organizationId=2";
        // A client-supplied forwarding header must not be trusted (ForwardedHeaders
        // middleware is not configured). Host is the only source.
        httpContext.Request.Headers["X-Forwarded-Host"] = "tenant2.punitbola.com";
        httpContext.Request.Headers["X-Forwarded-Organization"] = "2";

        var resolver = CreateResolver(context, httpContext);

        Assert.Equal(PikolballId, await resolver.ResolveOrganizationIdAsync());
    }

    [Fact]
    public async Task Tenant_CannotBeSelected_WhenHostInvalid_EvenIfClientClaimsOrganization()
    {
        var database = NewDatabase();
        await using var context = await SeedAsync(
            database,
            (PikolballId, "pikolball", OrganizationStatus.Active),
            (TenantBId, "tenant2", OrganizationStatus.Active));

        // Untrusted host, but the client tries every other channel to select tenant2.
        var httpContext = ContextWithHost("unknown.punitbola.com");
        httpContext.Request.QueryString = new QueryString("?organizationId=2");
        httpContext.Request.RouteValues = new RouteValueDictionary { ["organizationId"] = "2" };
        httpContext.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["organizationId"] = "2"
        });
        httpContext.Request.Headers["X-Organization-Id"] = "2";

        var resolver = CreateResolver(context, httpContext);

        Assert.Null(await resolver.ResolveOrganizationIdAsync());
    }

    // -------------------------------------------------------------------------
    // Authenticated authorization
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Authenticated_MemberOfHostTenant_Resolves()
    {
        var database = NewDatabase();
        await using var context = await SeedAsync(database, (PikolballId, "pikolball", OrganizationStatus.Active));
        context.OrganizationMembers.Add(new OrganizationMember { OrganizationId = PikolballId, UserId = "user-1", Role = OrganizationRole.OrganizationOwner });
        await context.SaveChangesAsync();

        var resolver = CreateResolver(context, AuthenticatedHttpContext("pikolball.punitbola.com", "user-1"));

        Assert.Equal(PikolballId, await resolver.ResolveOrganizationIdAsync());
    }

    [Fact]
    public async Task Authenticated_MemberOfOrgA_CannotAccessOrgB()
    {
        var database = NewDatabase();
        await using var context = await SeedAsync(
            database,
            (PikolballId, "pikolball", OrganizationStatus.Active),
            (TenantBId, "tenant2", OrganizationStatus.Active));
        context.OrganizationMembers.Add(new OrganizationMember { OrganizationId = PikolballId, UserId = "user-1", Role = OrganizationRole.OrganizationOwner });
        await context.SaveChangesAsync();

        var resolver = CreateResolver(context, AuthenticatedHttpContext("tenant2.punitbola.com", "user-1"));

        Assert.Null(await resolver.ResolveOrganizationIdAsync());
    }

    [Fact]
    public async Task Authenticated_WithoutMembership_ResolvesNothing()
    {
        var database = NewDatabase();
        await using var context = await SeedAsync(database, (PikolballId, "pikolball", OrganizationStatus.Active));

        var resolver = CreateResolver(context, AuthenticatedHttpContext("pikolball.punitbola.com", "no-membership"));

        Assert.Null(await resolver.ResolveOrganizationIdAsync());
    }

    [Fact]
    public async Task Authenticated_WithNoUserIdentifierClaim_ResolvesNothing()
    {
        var database = NewDatabase();
        await using var context = await SeedAsync(database, (PikolballId, "pikolball", OrganizationStatus.Active));

        // Authenticated principal that carries no NameIdentifier claim.
        var httpContext = ContextWithHost("pikolball.punitbola.com");
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, "someone") },
            authenticationType: "Test"));

        var resolver = CreateResolver(context, httpContext);

        Assert.Null(await resolver.ResolveOrganizationIdAsync());
    }

    // -------------------------------------------------------------------------
    // No request context (background / seeding scope)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NoHttpContext_ResolvesNothing()
    {
        var database = NewDatabase();
        await using var context = await SeedAsync(database, (PikolballId, "pikolball", OrganizationStatus.Active));

        var resolver = CreateResolver(context, null!);

        Assert.Null(await resolver.ResolveOrganizationIdAsync());
    }
}
