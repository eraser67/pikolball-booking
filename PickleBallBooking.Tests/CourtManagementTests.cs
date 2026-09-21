using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using Xunit;

namespace PickleBallBooking.Tests;

/// <summary>
/// Phase 24: in-memory unit tests for CourtService CRUD operations and
/// Phase 24 additions (SetImageAsync, RemoveImageAsync, auto CourtTimeSlot init).
/// All tests use isolated in-memory databases scoped to specific tenant IDs.
/// </summary>
public class CourtManagementTests
{
    // -----------------------------------------------------------------------
    // Create court
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_NewCourt_IsPersisted_WithTenantId()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("create_court_basic", organizationId: 10);
        var service = new CourtService(ctx);

        var court = await service.CreateAsync("Main Court", "Indoor");

        Assert.Equal("Main Court", court.Name);
        Assert.Equal("Indoor", court.Description);
        Assert.Equal(CourtStatus.Active, court.Status);
        Assert.Equal(10, court.OrganizationId);      // write guard stamps the tenant
        Assert.Null(court.ImagePath);                // no image on create
    }

    [Fact]
    public async Task CreateAsync_AutoInitializesCourtTimeSlots()
    {
        // Seed 3 global TimeSlots (normally 24, but 3 is enough to verify the logic).
        using var ctx = TestDbContextFactory.CreateInMemory("create_court_cts", organizationId: 10);

        ctx.TimeSlots.AddRange(
            new TimeSlot { StartTime = TimeSpan.FromHours(8),  EndTime = TimeSpan.FromHours(9),  Status = TimeSlotStatus.Active },
            new TimeSlot { StartTime = TimeSpan.FromHours(9),  EndTime = TimeSpan.FromHours(10), Status = TimeSlotStatus.Active },
            new TimeSlot { StartTime = TimeSpan.FromHours(10), EndTime = TimeSpan.FromHours(11), Status = TimeSlotStatus.Active }
        );
        await ctx.SaveChangesAsync();

        var service = new CourtService(ctx);
        var court = await service.CreateAsync("Court A", null);

        var slots = await ctx.CourtTimeSlots.Where(cts => cts.CourtId == court.Id).ToListAsync();
        Assert.Equal(3, slots.Count);
        Assert.All(slots, s => Assert.Equal(CourtTimeSlotStatus.Active, s.AvailabilityStatus));
        Assert.All(slots, s => Assert.Equal(10, s.OrganizationId));   // stamped by write guard
    }

    [Fact]
    public async Task CreateAsync_NoActiveTimeSlots_CreatesNoCourtTimeSlots()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("create_court_no_ts", organizationId: 10);
        // No TimeSlots seeded.
        var service = new CourtService(ctx);
        var court = await service.CreateAsync("Empty Court", null);

        var slots = await ctx.CourtTimeSlots.Where(cts => cts.CourtId == court.Id).ToListAsync();
        Assert.Empty(slots);
    }

    // -----------------------------------------------------------------------
    // Update court
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_ExistingCourt_UpdatesNameAndDescription()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("update_court", organizationId: 10);
        var service = new CourtService(ctx);
        var court = await service.CreateAsync("Old Name", "Old Desc");

        var result = await service.UpdateAsync(court.Id, "New Name", "New Desc");

        Assert.True(result);
        var updated = await ctx.Courts.FirstAsync();
        Assert.Equal("New Name", updated.Name);
        Assert.Equal("New Desc", updated.Description);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsFalse()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("update_unknown", organizationId: 10);
        var service = new CourtService(ctx);

        var result = await service.UpdateAsync(999, "X", null);
        Assert.False(result);
    }

    // -----------------------------------------------------------------------
    // Status toggle
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SetStatusAsync_CanDeactivate_AndReactivate()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("status_toggle", organizationId: 10);
        var service = new CourtService(ctx);
        var court = await service.CreateAsync("Court", null);
        Assert.Equal(CourtStatus.Active, court.Status);

        await service.SetStatusAsync(court.Id, CourtStatus.Inactive);
        var inactive = await ctx.Courts.FirstAsync();
        Assert.Equal(CourtStatus.Inactive, inactive.Status);

        await service.SetStatusAsync(court.Id, CourtStatus.Active);
        var active = await ctx.Courts.FirstAsync();
        Assert.Equal(CourtStatus.Active, active.Status);
    }

    [Fact]
    public async Task SetStatusAsync_DoesNotDeleteCourt()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("status_no_delete", organizationId: 10);
        var service = new CourtService(ctx);
        var court = await service.CreateAsync("Court", null);

        await service.SetStatusAsync(court.Id, CourtStatus.Inactive);

        Assert.Equal(1, await ctx.Courts.IgnoreQueryFilters().CountAsync());
    }

    // -----------------------------------------------------------------------
    // Image management (Phase 24)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SetImageAsync_ExistingCourt_SetsImagePath()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("set_image", organizationId: 10);
        var service = new CourtService(ctx);
        var court = await service.CreateAsync("Court", null);

        var result = await service.SetImageAsync(court.Id, "organizations/10/courts/1/main.jpg");

        Assert.True(result);
        var updated = await ctx.Courts.FirstAsync();
        Assert.Equal("organizations/10/courts/1/main.jpg", updated.ImagePath);
    }

    [Fact]
    public async Task RemoveImageAsync_ExistingCourt_ClearsImagePath()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("remove_image", organizationId: 10);
        var service = new CourtService(ctx);
        var court = await service.CreateAsync("Court", null);
        await service.SetImageAsync(court.Id, "organizations/10/courts/1/main.jpg");

        var result = await service.RemoveImageAsync(court.Id);

        Assert.True(result);
        var updated = await ctx.Courts.FirstAsync();
        Assert.Null(updated.ImagePath);
    }

    [Fact]
    public async Task SetImageAsync_UnknownId_ReturnsFalse()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("set_image_unknown", organizationId: 10);
        var service = new CourtService(ctx);

        var result = await service.SetImageAsync(999, "any/path.jpg");
        Assert.False(result);
    }

    [Fact]
    public async Task RemoveImageAsync_UnknownId_ReturnsFalse()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("remove_image_unknown", organizationId: 10);
        var service = new CourtService(ctx);

        var result = await service.RemoveImageAsync(999);
        Assert.False(result);
    }
}
