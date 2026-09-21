using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using Xunit;

namespace PickleBallBooking.Tests;

/// <summary>
/// Phase 24: tenant isolation tests for court management.
///
/// Covers the 7 isolation scenarios specified in Phase 24, §17, using in-memory
/// databases. All tests use the global EF Core query filter + write guard; no
/// IgnoreQueryFilters() is used in production paths.
/// </summary>
public class CourtTenantIsolationTests
{
    // -----------------------------------------------------------------------
    // Test 1: Tenant A sees only its own courts
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TenantA_CannotSee_TenantBCourts()
    {
        const string db = "isolation_test1";

        // Tenant B creates a court.
        using (var ctxB = TestDbContextFactory.CreateInMemory(db, organizationId: 2))
        {
            ctxB.Courts.Add(new Court
            {
                Name = "Tenant B Court",
                Status = CourtStatus.Active,
                OrganizationId = 2,
            });
            await ctxB.SaveChangesAsync();
        }

        // Tenant A queries — should see nothing.
        using var ctxA = TestDbContextFactory.CreateInMemory(db, organizationId: 1);
        var tenantACourts = await ctxA.Courts.ToListAsync();
        Assert.Empty(tenantACourts);
    }

    [Fact]
    public async Task TenantA_Sees_OnlyItsOwnCourts_WhenBothExist()
    {
        const string db = "isolation_test1b";

        // Both tenants create courts.
        using (var ctxA = TestDbContextFactory.CreateInMemory(db, organizationId: 1))
        {
            ctxA.Courts.Add(new Court { Name = "Court A", Status = CourtStatus.Active, OrganizationId = 1 });
            await ctxA.SaveChangesAsync();
        }
        using (var ctxB = TestDbContextFactory.CreateInMemory(db, organizationId: 2))
        {
            ctxB.Courts.Add(new Court { Name = "Court B", Status = CourtStatus.Active, OrganizationId = 2 });
            await ctxB.SaveChangesAsync();
        }

        using var readA = TestDbContextFactory.CreateInMemory(db, organizationId: 1);
        var courts = await readA.Courts.ToListAsync();
        Assert.Single(courts);
        Assert.Equal("Court A", courts[0].Name);
    }

    // -----------------------------------------------------------------------
    // Test 2: Tenant A cannot edit Tenant B's court
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TenantA_CannotEdit_TenantBCourt()
    {
        const string db = "isolation_test2";

        // Seed Tenant B court.
        int courtBId;
        using (var ctxB = TestDbContextFactory.CreateInMemory(db, organizationId: 2))
        {
            var court = new Court { Name = "B Court", Status = CourtStatus.Active, OrganizationId = 2 };
            ctxB.Courts.Add(court);
            await ctxB.SaveChangesAsync();
            courtBId = court.Id;
        }

        // Tenant A attempts UpdateAsync with Tenant B's court ID.
        using var ctxA = TestDbContextFactory.CreateInMemory(db, organizationId: 1);
        var serviceA = new CourtService(ctxA);

        // GetByIdAsync returns null (tenant query filter prevents cross-tenant visibility).
        var found = await serviceA.GetByIdAsync(courtBId);
        Assert.Null(found);

        // UpdateAsync also returns false (court not found under Tenant A's filter).
        var updated = await serviceA.UpdateAsync(courtBId, "Hacked Name", null);
        Assert.False(updated);

        // Verify Tenant B's court is unchanged.
        using var ctxBVerify = TestDbContextFactory.CreateInMemory(db, organizationId: 2);
        var courtB = await ctxBVerify.Courts.FirstAsync();
        Assert.Equal("B Court", courtB.Name);
    }

    // -----------------------------------------------------------------------
    // Test 3: Tenant A cannot change Tenant B's court status
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TenantA_CannotDeactivate_TenantBCourt()
    {
        const string db = "isolation_test3";

        int courtBId;
        using (var ctxB = TestDbContextFactory.CreateInMemory(db, organizationId: 2))
        {
            var court = new Court { Name = "B Court", Status = CourtStatus.Active, OrganizationId = 2 };
            ctxB.Courts.Add(court);
            await ctxB.SaveChangesAsync();
            courtBId = court.Id;
        }

        using var ctxA = TestDbContextFactory.CreateInMemory(db, organizationId: 1);
        var serviceA = new CourtService(ctxA);

        // SetStatusAsync on Tenant B's court ID should return false.
        var result = await serviceA.SetStatusAsync(courtBId, CourtStatus.Inactive);
        Assert.False(result);

        // Tenant B's court remains Active.
        using var ctxBVerify = TestDbContextFactory.CreateInMemory(db, organizationId: 2);
        var courtB = await ctxBVerify.Courts.FirstAsync();
        Assert.Equal(CourtStatus.Active, courtB.Status);
    }

    // -----------------------------------------------------------------------
    // Test 4: OrganizationId tampering is ignored by the write guard
    // -----------------------------------------------------------------------

    [Fact]
    public async Task WriteGuard_Stamps_ResolvedTenantId_IgnoringCallerValue()
    {
        const string db = "isolation_test4";

        using var ctxA = TestDbContextFactory.CreateInMemory(db, organizationId: 1);

        // Simulate a client attempting to submit OrganizationId = 99 (Tenant B).
        var court = new Court
        {
            Name           = "Tampered",
            OrganizationId = 99,   // client-supplied, should be overwritten
            Status         = CourtStatus.Active,
        };
        ctxA.Courts.Add(court);
        await ctxA.SaveChangesAsync();

        // The write guard must have overwritten OrganizationId with 1 (Tenant A).
        var saved = await ctxA.Courts.IgnoreQueryFilters().FirstAsync(c => c.Name == "Tampered");
        Assert.Equal(1, saved.OrganizationId);
        Assert.NotEqual(99, saved.OrganizationId);
    }

    // -----------------------------------------------------------------------
    // Test 5: Tenant A cannot manipulate Tenant B's image path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TenantA_CannotSetImage_OnTenantBCourt()
    {
        const string db = "isolation_test5_set";

        int courtBId;
        using (var ctxB = TestDbContextFactory.CreateInMemory(db, organizationId: 2))
        {
            var court = new Court { Name = "B Court", Status = CourtStatus.Active, OrganizationId = 2 };
            ctxB.Courts.Add(court);
            await ctxB.SaveChangesAsync();
            courtBId = court.Id;
        }

        using var ctxA = TestDbContextFactory.CreateInMemory(db, organizationId: 1);
        var serviceA = new CourtService(ctxA);

        var result = await serviceA.SetImageAsync(courtBId, "organizations/2/courts/1/main.jpg");
        Assert.False(result);   // query filter: court not found under Tenant A

        // Verify Tenant B's image is unchanged.
        using var ctxBVerify = TestDbContextFactory.CreateInMemory(db, organizationId: 2);
        var courtB = await ctxBVerify.Courts.FirstAsync();
        Assert.Null(courtB.ImagePath);
    }

    [Fact]
    public async Task TenantA_CannotRemoveImage_OnTenantBCourt()
    {
        const string db = "isolation_test5_remove";

        int courtBId;
        using (var ctxB = TestDbContextFactory.CreateInMemory(db, organizationId: 2))
        {
            var court = new Court
            {
                Name           = "B Court",
                Status         = CourtStatus.Active,
                OrganizationId = 2,
                ImagePath      = "organizations/2/courts/1/main.jpg",
            };
            ctxB.Courts.Add(court);
            await ctxB.SaveChangesAsync();
            courtBId = court.Id;
        }

        using var ctxA = TestDbContextFactory.CreateInMemory(db, organizationId: 1);
        var serviceA = new CourtService(ctxA);

        var result = await serviceA.RemoveImageAsync(courtBId);
        Assert.False(result);

        // Verify Tenant B's image path is still set.
        using var ctxBVerify = TestDbContextFactory.CreateInMemory(db, organizationId: 2);
        var courtB = await ctxBVerify.Courts.FirstAsync();
        Assert.Equal("organizations/2/courts/1/main.jpg", courtB.ImagePath);
    }

    // -----------------------------------------------------------------------
    // Test 6: Unknown tenant — no courts visible
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NoTenant_CannotSeeCourts()
    {
        const string db = "isolation_test6";

        using (var ctxA = TestDbContextFactory.CreateInMemory(db, organizationId: 1))
        {
            ctxA.Courts.Add(new Court { Name = "Court A", Status = CourtStatus.Active, OrganizationId = 1 });
            await ctxA.SaveChangesAsync();
        }

        using var noTenant = TestDbContextFactory.CreateWithoutTenant(db);
        var courts = await noTenant.Courts.ToListAsync();
        Assert.Empty(courts);
    }

    [Fact]
    public async Task NoTenant_WriteAttempt_ThrowsInvalidOperationException()
    {
        const string db = "isolation_test6_write";
        using var noTenant = TestDbContextFactory.CreateWithoutTenant(db);
        noTenant.Courts.Add(new Court { Name = "Unauthorized", Status = CourtStatus.Active });

        await Assert.ThrowsAsync<InvalidOperationException>(() => noTenant.SaveChangesAsync());
    }

    // -----------------------------------------------------------------------
    // Test 7: TimeSlot has no OrganizationId (compile-time + runtime)
    // -----------------------------------------------------------------------

    [Fact]
    public void TimeSlot_HasNoOrganizationId_Property()
    {
        // Compile-time guarantee: if OrganizationId were added to TimeSlot, this test
        // would still fail at the property-check level (belt-and-suspenders).
        var prop = typeof(TimeSlot).GetProperty("OrganizationId");
        Assert.Null(prop);
    }

    [Fact]
    public async Task TimeSlot_IsGlobal_VisibleWithoutTenant()
    {
        const string db = "isolation_test7";

        // Seed a TimeSlot without any tenant.
        using (var noTenant = TestDbContextFactory.CreateWithoutTenant(db))
        {
            noTenant.TimeSlots.Add(new TimeSlot
            {
                StartTime = TimeSpan.FromHours(8),
                EndTime   = TimeSpan.FromHours(9),
                Status    = TimeSlotStatus.Active,
            });
            await noTenant.SaveChangesAsync();
        }

        // Visible to Tenant A.
        using var ctxA = TestDbContextFactory.CreateInMemory(db, organizationId: 1);
        Assert.Equal(1, await ctxA.TimeSlots.CountAsync());

        // Visible to Tenant B.
        using var ctxB = TestDbContextFactory.CreateInMemory(db, organizationId: 2);
        Assert.Equal(1, await ctxB.TimeSlots.CountAsync());

        // Visible without a tenant.
        using var noTenantRead = TestDbContextFactory.CreateWithoutTenant(db);
        Assert.Equal(1, await noTenantRead.TimeSlots.CountAsync());
    }
}
