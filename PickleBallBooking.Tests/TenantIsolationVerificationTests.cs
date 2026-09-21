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
/// Phase 21 verification (read-only): exercises the EF Core behaviours that the
/// primary <see cref="TenantIsolationTests"/> do not explicitly cover, so the tenant
/// isolation claims can be validated precisely. No implementation code is changed.
///
/// Covered here:
///  - Navigation/Include filtering: a tenant-owned collection reached through an
///    <c>Include</c> (BookingTimeSlots on a Booking) is tenant-filtered.
///  - Tamper attempts: modifying or "adopting" a foreign row (including rewriting its
///    OrganizationId to the current tenant) is rejected by the write guard.
///  - Resolver determinism: current behaviour when a user belongs to more than one
///    organization is deterministic.
/// </summary>
public class TenantIsolationVerificationTests
{
    private const int TenantA = 1;
    private const int TenantB = 2;

    private static string NewDatabase() => Guid.NewGuid().ToString();

    private static async Task SeedAsync(string database, int organizationId, Action<ApplicationDbContext> seed)
    {
        await using var context = TestDbContextFactory.CreateInMemory(database, organizationId);
        seed(context);
        await context.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Navigation / Include filtering
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IncludedTenantOwnedCollection_IsTenantFiltered()
    {
        var database = NewDatabase();

        await SeedAsync(database, TenantA, ctx =>
        {
            var booking = NewBooking(TenantA, "PB-A");
            ctx.Bookings.Add(booking);
            ctx.BookingTimeSlots.Add(new BookingTimeSlot
            {
                OrganizationId = TenantA,
                Booking = booking,
                CourtId = 1,
                BookingDate = AppClock.TodayLocal,
                TimeSlotId = 1,
                SlotOrder = 0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        });

        await SeedAsync(database, TenantB, ctx =>
        {
            var booking = NewBooking(TenantB, "PB-B");
            ctx.Bookings.Add(booking);
            ctx.BookingTimeSlots.Add(new BookingTimeSlot
            {
                OrganizationId = TenantB,
                Booking = booking,
                CourtId = 2,
                BookingDate = AppClock.TodayLocal,
                TimeSlotId = 2,
                SlotOrder = 0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        });

        await using var tenantAContext = TestDbContextFactory.CreateInMemory(database, TenantA);

        // Tenant A's own booking includes only its own slots.
        var bookingA = await tenantAContext.Bookings
            .Include(b => b.TimeSlots)
            .SingleAsync(b => b.BookingReference == "PB-A");
        Assert.All(bookingA.TimeSlots, bts => Assert.Equal(TenantA, bts.OrganizationId));
        Assert.Single(bookingA.TimeSlots);

        // Tenant B's booking (root) is invisible, and therefore so are its slots.
        var bookingB = await tenantAContext.Bookings
            .Include(b => b.TimeSlots)
            .FirstOrDefaultAsync(b => b.BookingReference == "PB-B");
        Assert.Null(bookingB);

        // The included collection is also independently filtered when queried directly:
        // tenant A can never see tenant B's slot rows.
        var foreignSlots = await tenantAContext.BookingTimeSlots
            .Where(bts => bts.OrganizationId == TenantB)
            .ToListAsync();
        Assert.Empty(foreignSlots);

        // DOCUMENTED EF CORE SEMANTICS (verified, not a production path): a single
        // IgnoreQueryFilters() disables filters for the ENTIRE query - the root AND its
        // includes. So if a caller explicitly ignored filters, the foreign parent and its
        // children would both become visible. This is exactly why NO production code calls
        // IgnoreQueryFilters() (see verification report); isolation relies on production
        // queries never doing so. The behaviour is pinned here as a regression guard.
        var bookingBIgnoringFilter = await tenantAContext.Bookings
            .IgnoreQueryFilters()
            .Include(b => b.TimeSlots)
            .FirstOrDefaultAsync(b => b.BookingReference == "PB-B");

        Assert.NotNull(bookingBIgnoringFilter);
        Assert.Equal(TenantB, bookingBIgnoringFilter!.OrganizationId);
        Assert.Single(bookingBIgnoringFilter.TimeSlots);
    }

    [Fact]
    public async Task ServiceLookup_Includes_DoNotLeakCrossTenantSlots()
    {
        var database = NewDatabase();

        await SeedAsync(database, TenantB, ctx =>
        {
            var booking = NewBooking(TenantB, "PB-LOOKUP-B");
            ctx.Bookings.Add(booking);
            ctx.BookingTimeSlots.Add(new BookingTimeSlot
            {
                OrganizationId = TenantB,
                Booking = booking,
                CourtId = 7,
                BookingDate = AppClock.TodayLocal,
                TimeSlotId = 7,
                SlotOrder = 0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        });

        // A tenant-A service must not be able to look up tenant B's booking by reference.
        await using var tenantAContext = TestDbContextFactory.CreateInMemory(database, TenantA);
        var service = new BookingService(tenantAContext);

        var lookup = await service.LookupBookingAsync("PB-LOOKUP-B", "0900");
        Assert.Null(lookup);

        var byId = await service.GetBookingByIdAsync(1);
        Assert.Null(byId);
    }

    // -------------------------------------------------------------------------
    // Tamper attempts
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AdoptingAnotherTenantsRow_ByRewritingOrganizationId_IsRejected()
    {
        var database = NewDatabase();

        await SeedAsync(database, TenantB,
            ctx => ctx.Courts.Add(new Court { OrganizationId = TenantB, Name = "B Court", Status = CourtStatus.Active }));

        await using var tenantA = TestDbContextFactory.CreateInMemory(database, TenantA);

        // Simulate a leaked reference: load the foreign row ignoring the filter,
        // then try to "adopt" it by rewriting its OrganizationId to the current tenant.
        var foreignCourt = await tenantA.Courts.IgnoreQueryFilters().FirstAsync(c => c.OrganizationId == TenantB);
        foreignCourt.OrganizationId = TenantA;

        // The guard compares against the ORIGINAL owner, so adoption is rejected.
        await Assert.ThrowsAsync<InvalidOperationException>(() => tenantA.SaveChangesAsync());
    }

    [Fact]
    public async Task CrossTenantDelete_OfBookingTimeSlot_IsRejected()
    {
        var database = NewDatabase();

        await SeedAsync(database, TenantB, ctx =>
        {
            var booking = NewBooking(TenantB, "PB-B");
            ctx.Bookings.Add(booking);
            ctx.BookingTimeSlots.Add(new BookingTimeSlot
            {
                OrganizationId = TenantB,
                Booking = booking,
                CourtId = 2,
                BookingDate = AppClock.TodayLocal,
                TimeSlotId = 2,
                SlotOrder = 0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        });

        await using var tenantA = TestDbContextFactory.CreateInMemory(database, TenantA);
        var foreignSlot = await tenantA.BookingTimeSlots.IgnoreQueryFilters().FirstAsync(bts => bts.OrganizationId == TenantB);
        tenantA.BookingTimeSlots.Remove(foreignSlot);

        await Assert.ThrowsAsync<InvalidOperationException>(() => tenantA.SaveChangesAsync());
    }

    [Fact]
    public async Task CrossTenantDelete_OfCourtTimeSlot_IsRejected()
    {
        var database = NewDatabase();

        await SeedAsync(database, TenantB,
            ctx => ctx.CourtTimeSlots.Add(new CourtTimeSlot { OrganizationId = TenantB, CourtId = 2, TimeSlotId = 2 }));

        await using var tenantA = TestDbContextFactory.CreateInMemory(database, TenantA);
        var foreignSlot = await tenantA.CourtTimeSlots.IgnoreQueryFilters().FirstAsync(cts => cts.OrganizationId == TenantB);
        tenantA.CourtTimeSlots.Remove(foreignSlot);

        await Assert.ThrowsAsync<InvalidOperationException>(() => tenantA.SaveChangesAsync());
    }

    // -------------------------------------------------------------------------
    // Resolver: hostname selects the tenant; membership authorizes it
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Resolver_MultiMembershipUser_ResolvesToTheHostnameIdentifiedTenant()
    {
        var database = NewDatabase();

        await using var context = TestDbContextFactory.CreateWithoutTenant(database);
        // The schema (unique IX_OrganizationMember_OrganizationId_UserId) allows a user
        // to belong to more than one organization.
        context.Organizations.AddRange(
            new Organization { Id = TenantA, Name = "A", Slug = "tenanta", Status = OrganizationStatus.Active },
            new Organization { Id = TenantB, Name = "B", Slug = "tenantb", Status = OrganizationStatus.Active });
        context.OrganizationMembers.AddRange(
            new OrganizationMember { OrganizationId = TenantA, UserId = "multi", Role = OrganizationRole.OrganizationStaff },
            new OrganizationMember { OrganizationId = TenantB, UserId = "multi", Role = OrganizationRole.OrganizationStaff });
        await context.SaveChangesAsync();

        // The hostname is the selector: the same multi-membership user resolves to A on
        // A's host and to B on B's host - and never to both at once.
        var onA = CreateResolver(context, "tenanta.punitbola.com", AuthenticatedContext("multi"));
        Assert.Equal(TenantA, await onA.ResolveOrganizationIdAsync());

        var onB = CreateResolver(context, "tenantb.punitbola.com", AuthenticatedContext("multi"));
        Assert.Equal(TenantB, await onB.ResolveOrganizationIdAsync());
    }

    [Fact]
    public async Task Resolver_MultiMembershipUser_IsRejectedOnNonMemberHost()
    {
        var database = NewDatabase();

        await using var context = TestDbContextFactory.CreateWithoutTenant(database);
        context.Organizations.AddRange(
            new Organization { Id = TenantA, Name = "A", Slug = "tenanta", Status = OrganizationStatus.Active },
            new Organization { Id = TenantB, Name = "B", Slug = "tenantb", Status = OrganizationStatus.Active });
        context.OrganizationMembers.Add(
            new OrganizationMember { OrganizationId = TenantA, UserId = "multi", Role = OrganizationRole.OrganizationStaff });
        await context.SaveChangesAsync();

        // Even though the host is a valid tenant, this user is only a member of A.
        var onB = CreateResolver(context, "tenantb.punitbola.com", AuthenticatedContext("multi"));
        Assert.Null(await onB.ResolveOrganizationIdAsync());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private const string BaseDomain = "punitbola.com";

    private static TenantResolver CreateResolver(ApplicationDbContext context, string host, HttpContext? httpContext = null)
    {
        var ctx = httpContext ?? new DefaultHttpContext();
        ctx.Request.Host = new HostString(host);
        return new TenantResolver(
            new HttpContextAccessor { HttpContext = ctx },
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
}
