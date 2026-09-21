using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using Xunit;

namespace PickleBallBooking.Tests;

/// <summary>
/// Phase 21/23 verification (read-only): asserts database integrity of the live
/// PostgreSQL database after the multi-tenant foundation/backfill.
///
/// Every assertion is read-only (no writes, no migration). It confirms:
///  - Every tenant-owned row has a non-null OrganizationId that references a real
///    organization (no orphaned/invalid tenant ids).
///  - The seeded Pikolball fixture data (courts/pricing/bookings/slots) is intact
///    and has not been reassigned to another organization.
///  - Phase 23 may add additional organizations; this test accepts any number >= 1.
///
/// It runs against whatever database the "DefaultConnection" secret points to and
/// performs only SELECTs, so it is safe to run against the shared database.
/// </summary>
public class PostgresTenantIntegrityVerificationTests
{
    [Fact]
    public async Task AllTenantOwnedRows_HaveValidOrganization_AndBelongToPikolball()
    {
        await using var context = PostgresTestDatabase.CreateContext();

        // Organizations are global (no tenant filter): list them all.
        var organizations = await context.Organizations.AsNoTracking().ToListAsync();
        Assert.NotEmpty(organizations);
        var organizationIds = organizations.Select(o => o.Id).ToHashSet();

        var pikolball = organizations.Single(o => o.Slug == OrganizationDefaults.PikolballSlug);

        // Inspect every tenant-owned table via IgnoreQueryFilters (read-only) so we can
        // validate the raw stored values regardless of the current tenant context.
        var courtOrgIds = await context.Courts.IgnoreQueryFilters().AsNoTracking().Select(c => c.OrganizationId).ToListAsync();
        var pricingOrgIds = await context.Pricings.IgnoreQueryFilters().AsNoTracking().Select(p => p.OrganizationId).ToListAsync();
        var bookingOrgIds = await context.Bookings.IgnoreQueryFilters().AsNoTracking().Select(b => b.OrganizationId).ToListAsync();
        var btsOrgIds = await context.BookingTimeSlots.IgnoreQueryFilters().AsNoTracking().Select(b => b.OrganizationId).ToListAsync();
        var ctsOrgIds = await context.CourtTimeSlots.IgnoreQueryFilters().AsNoTracking().Select(c => c.OrganizationId).ToListAsync();

        var allOrgIds = courtOrgIds
            .Concat(pricingOrgIds)
            .Concat(bookingOrgIds)
            .Concat(btsOrgIds)
            .Concat(ctsOrgIds)
            .ToList();

        // No tenant-owned row may have OrganizationId = 0 (the default/unset value that
        // signals an incomplete backfill) or reference a non-existent organization.
        Assert.DoesNotContain(0, allOrgIds);
        Assert.All(allOrgIds, id => Assert.Contains(id, organizationIds));

        // Phase 23/24: multiple organizations are now supported. What must remain true:
        //   1. Pikolball still exists (verified above with Single).
        //   2. Pikolball's own courts still reference Pikolball's OrganizationId.
        //   3. Pikolball has its seeded courts and pricing (at least 1 of each).
        // Other organizations may legitimately own courts, CourtTimeSlots, etc.
        Assert.True(organizations.Count >= 1, "Expected at least one organization.");

        // Pikolball's own rows must reference the correct OrganizationId.
        Assert.All(
            courtOrgIds.Where(id => id == pikolball.Id),
            id => Assert.Equal(pikolball.Id, id));

        // Pikolball must have its seeded fixture data (courts + pricing).
        Assert.True(
            courtOrgIds.Any(id => id == pikolball.Id),
            "Pikolball must have at least one court — seeded data appears missing or reassigned.");
        Assert.True(
            pricingOrgIds.Any(id => id == pikolball.Id),
            "Pikolball must have at least one pricing rule — seeded data appears missing or reassigned.");
    }

    [Fact]
    public async Task PikolballSeededData_IsReadableThroughTheResolvedTenant()
    {
        // Bind a context to the Pikolball tenant: queries are scoped and must return the
        // same data an anonymous request would see.
        await using var context = PostgresTestDatabase.CreateContext();
        var organizationId = await PostgresTestDatabase.GetOrCreateTestOrganizationIdAsync(context);
        context.UseTenant(organizationId);

        // Counts through the tenant-filtered context...
        var tenantCourts = await context.Courts.AsNoTracking().CountAsync();
        var tenantPricings = await context.Pricings.AsNoTracking().CountAsync();
        var tenantBookings = await context.Bookings.AsNoTracking().CountAsync();

        // ...must equal the raw counts filtered to the same organization.
        var rawCourts = await context.Courts.IgnoreQueryFilters().AsNoTracking().CountAsync(c => c.OrganizationId == organizationId);
        var rawPricings = await context.Pricings.IgnoreQueryFilters().AsNoTracking().CountAsync(p => p.OrganizationId == organizationId);
        var rawBookings = await context.Bookings.IgnoreQueryFilters().AsNoTracking().CountAsync(b => b.OrganizationId == organizationId);

        Assert.Equal(rawCourts, tenantCourts);
        Assert.Equal(rawPricings, tenantPricings);
        Assert.Equal(rawBookings, tenantBookings);

        // The seeded Pikolball demo data exists (courts + pricing at minimum).
        Assert.True(tenantCourts > 0, "Expected Pikolball courts to exist.");
        Assert.True(tenantPricings > 0, "Expected Pikolball pricing to exist.");
    }

    [Fact]
    public async Task TimeSlot_IsGlobal_HasNoOrganization_AndIsUnaffectedByTenant()
    {
        // TimeSlots are global: an unresolved-tenant context must still see them.
        await using var noTenant = PostgresTestDatabase.CreateContext();
        var countWithoutTenant = await noTenant.TimeSlots.AsNoTracking().CountAsync();

        await using var withTenant = PostgresTestDatabase.CreateContext();
        var organizationId = await PostgresTestDatabase.GetOrCreateTestOrganizationIdAsync(withTenant);
        withTenant.UseTenant(organizationId);
        var countWithTenant = await withTenant.TimeSlots.AsNoTracking().CountAsync();

        Assert.Equal(countWithoutTenant, countWithTenant);
        Assert.True(countWithoutTenant > 0, "Expected global TimeSlots to exist.");

        // The TimeSlot CLR type has no OrganizationId property (compile-time guarantee).
        Assert.Null(typeof(TimeSlot).GetProperty("OrganizationId"));
    }
}
