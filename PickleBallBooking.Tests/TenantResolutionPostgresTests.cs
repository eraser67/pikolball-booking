using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using Xunit;

namespace PickleBallBooking.Tests;

/// <summary>
/// Phase 22: hostname resolution parity against the real PostgreSQL database.
///
/// These confirm the resolver works against the actual seeded Pikolball organization
/// (no hard-coded ids) and correctly rejects unknown/inactive tenants. Rows added by
/// a test are wrapped in a transaction that is rolled back, so the shared database is
/// never mutated. Read-only assertions rely on the seeded "pikolball" organization.
/// </summary>
public class TenantResolutionPostgresTests
{
    private const string BaseDomain = "punitbola.com";

    private static TenantResolver CreateResolver(
        Data.ApplicationDbContext context,
        string host,
        HttpContext? httpContext = null)
    {
        var ctx = httpContext ?? new DefaultHttpContext();
        ctx.Request.Host = new HostString(host);
        return new TenantResolver(
            new HttpContextAccessor { HttpContext = ctx },
            context,
            new TenantHostParser(),
            Options.Create(new TenantOptions { BaseDomain = BaseDomain }));
    }

    [Fact]
    public async Task RealPikolballOrganization_ResolvesFromItsSubdomain()
    {
        await using var context = PostgresTestDatabase.CreateContext();
        var pikolballId = await PostgresTestDatabase.GetOrCreateTestOrganizationIdAsync(context);

        var resolver = CreateResolver(context, "pikolball.punitbola.com");

        Assert.Equal(pikolballId, await resolver.ResolveOrganizationIdAsync());
    }

    [Fact]
    public async Task BaseDomainAndWww_DoNotResolve_AgainstRealDatabase()
    {
        await using var context = PostgresTestDatabase.CreateContext();
        await PostgresTestDatabase.GetOrCreateTestOrganizationIdAsync(context);

        Assert.Null(await CreateResolver(context, "punitbola.com").ResolveOrganizationIdAsync());
        Assert.Null(await CreateResolver(context, "www.punitbola.com").ResolveOrganizationIdAsync());
    }

    [Fact]
    public async Task UnknownSubdomain_DoesNotFallBackToPikolball_AgainstRealDatabase()
    {
        await using var context = PostgresTestDatabase.CreateContext();
        await PostgresTestDatabase.GetOrCreateTestOrganizationIdAsync(context);

        var resolver = CreateResolver(context, "definitely-not-a-real-tenant.punitbola.com");

        Assert.Null(await resolver.ResolveOrganizationIdAsync());
    }

    [Fact]
    public async Task InactiveOrganization_DoesNotResolve_AgainstRealDatabase()
    {
        await using var context = PostgresTestDatabase.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var slug = $"inactive-{Guid.NewGuid():N}";
        context.Organizations.Add(new Organization
        {
            Name = "Inactive Tenant",
            Slug = slug,
            Status = OrganizationStatus.Inactive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var resolver = CreateResolver(context, $"{slug}.punitbola.com");

        Assert.Null(await resolver.ResolveOrganizationIdAsync());

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task AuthenticatedNonMember_DoesNotResolve_RealPikolballHost()
    {
        await using var context = PostgresTestDatabase.CreateContext();
        await PostgresTestDatabase.GetOrCreateTestOrganizationIdAsync(context);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("pikolball.punitbola.com");
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, $"nonmember-{Guid.NewGuid():N}") },
                authenticationType: "Test"));

        var resolver = CreateResolver(context, "pikolball.punitbola.com", httpContext);

        Assert.Null(await resolver.ResolveOrganizationIdAsync());
    }
}
