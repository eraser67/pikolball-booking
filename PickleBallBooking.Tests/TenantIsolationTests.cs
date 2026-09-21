using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using Xunit;

namespace PickleBallBooking.Tests;

/// <summary>
/// Phase 21/22: tenant isolation tests.
///
/// These prove the two production mechanisms that enforce tenant boundaries:
///  1. EF Core global query filters scope every tenant-owned read to the current
///     organization (and hide everything when no tenant is resolved).
///  2. The DbContext write guard stamps new tenant-owned rows with the resolved
///     organization and rejects cross-tenant modifications/deletions.
///
/// Plus the server-side resolver that derives the tenant from the request HOSTNAME
/// (Phase 22) and enforces membership for authenticated users - never from
/// query/route/form/cookie/body or a client-supplied organization id.
///
/// These run on the EF Core InMemory provider: query filters and the write guard are
/// provider-independent, so the isolation guarantees are exercised without a database.
/// </summary>
public class TenantIsolationTests
{
    private const int TenantA = 1;
    private const int TenantB = 2;

    private static string NewDatabase() => Guid.NewGuid().ToString();

    /// <summary>
    /// Seeds rows through a context bound to <paramref name="organizationId"/>. This
    /// matters because the write guard stamps every added tenant-owned row with the
    /// resolved tenant, so multi-tenant fixtures must seed each tenant via a context
    /// bound to that tenant (exactly as production writes work).
    /// </summary>
    private static async Task SeedAsync(string database, int organizationId, Action<ApplicationDbContext> seed)
    {
        await using var context = TestDbContextFactory.CreateInMemory(database, organizationId);
        seed(context);
        await context.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Read isolation (global query filters)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Courts_TenantASeesOnlyItsOwnRows()
    {
        var database = NewDatabase();

        await SeedAsync(database, TenantA,
            c => c.Courts.Add(new Court { OrganizationId = TenantA, Name = "A Court", Status = CourtStatus.Active }));
        await SeedAsync(database, TenantB,
            c => c.Courts.Add(new Court { OrganizationId = TenantB, Name = "B Court", Status = CourtStatus.Active }));

        await using var tenantA = TestDbContextFactory.CreateInMemory(database, TenantA);
        var names = await tenantA.Courts.Select(c => c.Name).ToListAsync();

        Assert.Single(names);
        Assert.Equal("A Court", names[0]);
    }

    [Fact]
    public async Task Pricing_And_Bookings_AreScopedToTheCurrentTenant()
    {
        var database = NewDatabase();

        await SeedAsync(database, TenantA, seed =>
        {
            seed.Pricings.Add(new Pricing { OrganizationId = TenantA, DayType = DayType.Weekday, Price = 100m, Status = PricingStatus.Active });
            seed.Bookings.Add(NewBooking(TenantA, "PB-A"));
        });
        await SeedAsync(database, TenantB, seed =>
        {
            seed.Pricings.Add(new Pricing { OrganizationId = TenantB, DayType = DayType.Weekday, Price = 999m, Status = PricingStatus.Active });
            seed.Bookings.Add(NewBooking(TenantB, "PB-B"));
        });

        await using var tenantB = TestDbContextFactory.CreateInMemory(database, TenantB);

        Assert.Equal(999m, (await tenantB.Pricings.SingleAsync()).Price);
        Assert.Single(await tenantB.Bookings.ToListAsync());
        Assert.Equal("PB-B", (await tenantB.Bookings.SingleAsync()).BookingReference);
    }

    [Fact]
    public async Task BookingTimeSlots_And_CourtTimeSlots_AreScopedToTheCurrentTenant()
    {
        var database = NewDatabase();

        await SeedAsync(database, TenantA, seed =>
        {
            seed.CourtTimeSlots.Add(new CourtTimeSlot { OrganizationId = TenantA, CourtId = 1, TimeSlotId = 1 });
            seed.BookingTimeSlots.Add(NewBookingTimeSlot(TenantA, courtId: 1));
        });
        await SeedAsync(database, TenantB, seed =>
        {
            seed.CourtTimeSlots.Add(new CourtTimeSlot { OrganizationId = TenantB, CourtId = 2, TimeSlotId = 2 });
            seed.BookingTimeSlots.Add(NewBookingTimeSlot(TenantB, courtId: 2));
        });

        await using var tenantA = TestDbContextFactory.CreateInMemory(database, TenantA);

        Assert.Single(await tenantA.CourtTimeSlots.ToListAsync());
        Assert.Equal(1, (await tenantA.BookingTimeSlots.SingleAsync()).CourtId);
    }

    [Fact]
    public async Task UnresolvedTenant_SeesNoTenantOwnedData()
    {
        var database = NewDatabase();

        await SeedAsync(database, TenantA, seed =>
        {
            seed.Courts.Add(new Court { OrganizationId = TenantA, Name = "A Court", Status = CourtStatus.Active });
            seed.Pricings.Add(new Pricing { OrganizationId = TenantA, DayType = DayType.Weekday, Price = 100m, Status = PricingStatus.Active });
            seed.Bookings.Add(NewBooking(TenantA, "PB-A"));
        });

        await using var noTenant = TestDbContextFactory.CreateWithoutTenant(database);

        Assert.Empty(await noTenant.Courts.ToListAsync());
        Assert.Empty(await noTenant.Pricings.ToListAsync());
        Assert.Empty(await noTenant.Bookings.ToListAsync());
    }

    [Fact]
    public async Task GlobalEntities_AreNotFilteredByTenant()
    {
        var database = NewDatabase();

        await SeedAsync(database, TenantA, seed =>
        {
            seed.TimeSlots.Add(new TimeSlot { StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(10, 0, 0), Status = TimeSlotStatus.Active });
            seed.Organizations.Add(new Organization { Name = "A", Slug = "a", Status = OrganizationStatus.Active });
        });

        await using var noTenant = TestDbContextFactory.CreateWithoutTenant(database);

        // TimeSlots and Organizations are global/shared and stay visible with no tenant.
        Assert.Single(await noTenant.TimeSlots.ToListAsync());
        Assert.Single(await noTenant.Organizations.ToListAsync());
    }

    // -------------------------------------------------------------------------
    // Write isolation (write guard)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NewTenantOwnedRow_IsStampedWithTheResolvedTenant()
    {
        var database = NewDatabase();

        await using var tenantB = TestDbContextFactory.CreateInMemory(database, TenantB);

        // Even if a caller deliberately sets the wrong tenant, the guard overwrites it.
        var court = new Court { OrganizationId = TenantA, Name = "Forged", Status = CourtStatus.Active };
        tenantB.Courts.Add(court);
        await tenantB.SaveChangesAsync();

        Assert.Equal(TenantB, court.OrganizationId);
    }

    [Fact]
    public async Task ModifyingAnotherTenantsRow_IsRejected()
    {
        var database = NewDatabase();

        await SeedAsync(database, TenantB,
            seed => seed.Courts.Add(new Court { OrganizationId = TenantB, Name = "B Court", Status = CourtStatus.Active }));

        await using var tenantA = TestDbContextFactory.CreateInMemory(database, TenantA);

        // Load the row while ignoring the tenant filter to simulate a leaked reference,
        // then attempt to modify it. The guard must reject the cross-tenant write.
        var foreignCourt = await tenantA.Courts.IgnoreQueryFilters().FirstAsync(c => c.OrganizationId == TenantB);
        foreignCourt.Name = "Hijacked";

        await Assert.ThrowsAsync<InvalidOperationException>(() => tenantA.SaveChangesAsync());
    }

    [Fact]
    public async Task DeletingAnotherTenantsRow_IsRejected()
    {
        var database = NewDatabase();

        await SeedAsync(database, TenantB,
            seed => seed.Pricings.Add(new Pricing { OrganizationId = TenantB, DayType = DayType.Weekday, Price = 100m, Status = PricingStatus.Active }));

        await using var tenantA = TestDbContextFactory.CreateInMemory(database, TenantA);

        var foreignPricing = await tenantA.Pricings.IgnoreQueryFilters().FirstAsync(p => p.OrganizationId == TenantB);
        tenantA.Pricings.Remove(foreignPricing);

        await Assert.ThrowsAsync<InvalidOperationException>(() => tenantA.SaveChangesAsync());
    }

    [Fact]
    public async Task WritingTenantOwnedData_WithNoResolvedTenant_IsRejected()
    {
        var database = NewDatabase();

        await using var noTenant = TestDbContextFactory.CreateWithoutTenant(database);
        noTenant.Courts.Add(new Court { Name = "Orphan", Status = CourtStatus.Active });

        await Assert.ThrowsAsync<InvalidOperationException>(() => noTenant.SaveChangesAsync());
    }

    // -------------------------------------------------------------------------
    // Server-side tenant resolution
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Resolver_ReturnsMembershipOrganization_ForAuthenticatedUser_OnMatchingHost()
    {
        var database = NewDatabase();

        await using var context = TestDbContextFactory.CreateWithoutTenant(database);
        context.Organizations.Add(new Organization { Id = TenantB, Name = "B", Slug = "tenantb", Status = OrganizationStatus.Active });
        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = TenantB,
            UserId = "user-b",
            Role = OrganizationRole.OrganizationOwner
        });
        await context.SaveChangesAsync();

        var resolver = CreateResolver(
            context,
            host: "tenantb.punitbola.com",
            user: AuthenticatedContext("user-b"));

        Assert.Equal(TenantB, await resolver.ResolveOrganizationIdAsync());
    }

    [Fact]
    public async Task Resolver_ReturnsNull_ForAuthenticatedUserWithoutMembership()
    {
        var database = NewDatabase();

        await using var context = TestDbContextFactory.CreateWithoutTenant(database);
        context.Organizations.Add(new Organization { Id = TenantB, Name = "B", Slug = "tenantb", Status = OrganizationStatus.Active });
        await context.SaveChangesAsync();

        // The host resolves to a real tenant, but the user is not a member of it.
        var resolver = CreateResolver(
            context,
            host: "tenantb.punitbola.com",
            user: AuthenticatedContext("user-with-no-membership"));

        Assert.Null(await resolver.ResolveOrganizationIdAsync());
    }

    [Fact]
    public async Task Resolver_AuthenticatedUserOfOtherOrg_CannotResolveForeignHost()
    {
        var database = NewDatabase();

        await using var context = TestDbContextFactory.CreateWithoutTenant(database);
        context.Organizations.AddRange(
            new Organization { Id = TenantA, Name = "A", Slug = "tenanta", Status = OrganizationStatus.Active },
            new Organization { Id = TenantB, Name = "B", Slug = "tenantb", Status = OrganizationStatus.Active });
        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = TenantA,
            UserId = "user-a",
            Role = OrganizationRole.OrganizationOwner
        });
        await context.SaveChangesAsync();

        // User belongs to A; visiting B's subdomain must NOT resolve tenant B.
        var resolverOnB = CreateResolver(
            context,
            host: "tenantb.punitbola.com",
            user: AuthenticatedContext("user-a"));
        Assert.Null(await resolverOnB.ResolveOrganizationIdAsync());

        // The same user on their own tenant's host resolves correctly.
        var resolverOnA = CreateResolver(
            context,
            host: "tenanta.punitbola.com",
            user: AuthenticatedContext("user-a"));
        Assert.Equal(TenantA, await resolverOnA.ResolveOrganizationIdAsync());
    }

    [Fact]
    public async Task Resolver_ResolvesActiveTenant_ForAnonymousUser_ByHostname()
    {
        var database = NewDatabase();

        await using var context = TestDbContextFactory.CreateWithoutTenant(database);
        context.Organizations.Add(new Organization { Id = TenantA, Name = "A", Slug = "pikolball", Status = OrganizationStatus.Active });
        await context.SaveChangesAsync();

        var resolver = CreateResolver(context, host: "pikolball.punitbola.com");

        Assert.Equal(TenantA, await resolver.ResolveOrganizationIdAsync());
    }

    [Fact]
    public async Task Resolver_DoesNotFallBack_ForAnonymousUnknownHost()
    {
        var database = NewDatabase();

        await using var context = TestDbContextFactory.CreateWithoutTenant(database);
        context.Organizations.Add(new Organization { Id = TenantA, Name = "A", Slug = "pikolball", Status = OrganizationStatus.Active });
        await context.SaveChangesAsync();

        // An unknown subdomain must NOT fall back to Pikolball.
        var resolver = CreateResolver(context, host: "unknown.punitbola.com");

        Assert.Null(await resolver.ResolveOrganizationIdAsync());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private const string BaseDomain = "punitbola.com";

    /// <summary>
    /// Builds a Phase 22 resolver bound to an in-memory context with the given Host
    /// header. Reads only the request host, so this exercises the real resolution path.
    /// </summary>
    private static TenantResolver CreateResolver(ApplicationDbContext context, string host, HttpContext? user = null)
    {
        var httpContext = user ?? new DefaultHttpContext();
        httpContext.Request.Host = new HostString(host);
        return new TenantResolver(
            new HttpContextAccessor { HttpContext = httpContext },
            context,
            new TenantHostParser(),
            Microsoft.Extensions.Options.Options.Create(new TenantOptions { BaseDomain = BaseDomain }));
    }

    private static HttpContext AuthenticatedContext(string userId)
    {
        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId)
        }, authenticationType: "Test");
        context.User = new ClaimsPrincipal(identity);
        return context;
    }

    private static Booking NewBooking(int organizationId, string reference) => new()
    {
        OrganizationId = organizationId,
        BookingReference = reference,
        CustomerName = "Customer",
        CustomerPhone = "0900",
        CustomerEmail = "customer@example.com",
        CourtId = 1,
        BookingDate = AppClock.TodayLocal,
        StartTime = new TimeSpan(9, 0, 0),
        EndTime = new TimeSpan(10, 0, 0),
        DurationHours = 1m,
        Price = 100m,
        BookingStatus = BookingStatus.Confirmed,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static BookingTimeSlot NewBookingTimeSlot(int organizationId, int courtId) => new()
    {
        OrganizationId = organizationId,
        BookingId = courtId,
        CourtId = courtId,
        BookingDate = AppClock.TodayLocal,
        TimeSlotId = courtId,
        SlotOrder = 0,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
