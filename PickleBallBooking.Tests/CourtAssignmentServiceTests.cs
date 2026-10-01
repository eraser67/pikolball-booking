using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using Xunit;

namespace PickleBallBooking.Tests;

public class CourtAssignmentServiceTests
{
    private sealed class FakeCourtImageStorage : ICourtImageStorage
    {
        public Task<string> UploadAsync(int organizationId, int courtId, IFormFile file, CancellationToken ct = default) => Task.FromResult("path");
        public Task<string> UploadLogoAsync(int organizationId, IFormFile file, CancellationToken ct = default) => Task.FromResult("path");
        public Task<string> UploadHeroImageAsync(int organizationId, IFormFile file, CancellationToken ct = default) => Task.FromResult("path");
        public Task<string> UploadAvatarAsync(string userId, IFormFile file, CancellationToken ct = default) => Task.FromResult("path");
        public Task DeleteAsync(string storagePath, CancellationToken ct = default) => Task.CompletedTask;
        public string? GetPublicUrl(string? storagePath) => storagePath is not null ? $"https://storage.test/{storagePath}" : null;
    }

    private static CourtAssignmentService CreateService(ApplicationDbContext ctx)
    {
        var notifService = new AppNotificationService(ctx, NullLogger<AppNotificationService>.Instance);
        var storage = new FakeCourtImageStorage();
        return new CourtAssignmentService(ctx, storage, notifService);
    }

    private static async Task<(Activity Activity, Court Court1, Court Court2, List<ActivityRsvp> Rsvps)> SeedActivityWithCourtsAndPlayersAsync(
        ApplicationDbContext ctx,
        int orgId = 1,
        int playerCount = 4)
    {
        var court1 = new Court { OrganizationId = orgId, Name = "Court 1", Status = CourtStatus.Active };
        var court2 = new Court { OrganizationId = orgId, Name = "Court 2", Status = CourtStatus.Active };
        ctx.Courts.AddRange(court1, court2);

        var activity = new Activity
        {
            OrganizationId = orgId,
            Name = "Open Play Saturday",
            Date = DateOnly.FromDateTime(DateTime.Today),
            StartTime = TimeSpan.FromHours(8),
            EndTime = TimeSpan.FromHours(10),
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(activity);
        await ctx.SaveChangesAsync();

        var ac1 = new ActivityCourt { OrganizationId = orgId, ActivityId = activity.Id, CourtId = court1.Id };
        var ac2 = new ActivityCourt { OrganizationId = orgId, ActivityId = activity.Id, CourtId = court2.Id };
        ctx.ActivityCourts.AddRange(ac1, ac2);

        var rsvps = new List<ActivityRsvp>();
        for (int i = 1; i <= playerCount; i++)
        {
            var userId = $"player-{i}";
            var rsvp = new ActivityRsvp
            {
                OrganizationId = orgId,
                ActivityId = activity.Id,
                UserId = userId,
                Status = RsvpStatus.Confirmed
            };
            rsvps.Add(rsvp);

            // Add player profile with skill level
            var skill = (i % 3) switch
            {
                0 => PlayerSkillLevel.Advanced,
                1 => PlayerSkillLevel.Intermediate,
                _ => PlayerSkillLevel.Beginner
            };
            ctx.PlayerProfiles.Add(new PlayerProfile
            {
                UserId = userId,
                FirstName = $"Player{i}",
                LastName = "Test",
                DisplayName = $"Player {i}",
                SkillLevel = skill
            });
        }
        ctx.ActivityRsvps.AddRange(rsvps);
        await ctx.SaveChangesAsync();

        return (activity, court1, court2, rsvps);
    }

    [Fact]
    public async Task GetOverviewAsync_ReturnsNull_WhenActivityDoesNotExist()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("overview_not_found", organizationId: 1);
        var service = CreateService(ctx);

        var result = await service.GetOverviewAsync(999);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetOverviewAsync_ReturnsCorrectCourtsAndUnassignedPlayers()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("overview_success", organizationId: 1);
        var service = CreateService(ctx);
        var (activity, court1, _, rsvps) = await SeedActivityWithCourtsAndPlayersAsync(ctx, orgId: 1, playerCount: 4);

        // Assign player 1 to Court 1
        await service.AssignPlayerAsync(activity.Id, rsvps[0].Id, court1.Id, 1, "admin");

        var overview = await service.GetOverviewAsync(activity.Id);
        Assert.NotNull(overview);
        Assert.Equal(activity.Name, overview.ActivityName);
        Assert.Equal(2, overview.Courts.Count);
        Assert.Equal(4, overview.TotalConfirmedCount);
        Assert.Equal(1, overview.TotalAssignedCount);
        Assert.Equal(3, overview.UnassignedPlayers.Count);

        var c1Group = overview.Courts.First(c => c.CourtId == court1.Id);
        Assert.Single(c1Group.Players);
        Assert.Equal("Player 1", c1Group.Players[0].PlayerName);
    }

    [Fact]
    public async Task AssignPlayerAsync_Succeeds_WhenValidCourtAndPlayer()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("assign_success", organizationId: 1);
        var service = CreateService(ctx);
        var (activity, court1, _, rsvps) = await SeedActivityWithCourtsAndPlayersAsync(ctx, orgId: 1, playerCount: 2);

        var result = await service.AssignPlayerAsync(activity.Id, rsvps[0].Id, court1.Id, 1, "admin-1");

        Assert.True(result.Success);
        var assignment = await ctx.ActivityCourtAssignments.FirstOrDefaultAsync(ca => ca.ActivityRsvpId == rsvps[0].Id);
        Assert.NotNull(assignment);
        Assert.Equal(court1.Id, assignment.CourtId);
        Assert.Equal(1, assignment.SlotNumber);
        Assert.Equal("admin-1", assignment.AssignedByUserId);
    }

    [Fact]
    public async Task AssignPlayerAsync_Fails_WhenActivityLocked()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("assign_locked", organizationId: 1);
        var service = CreateService(ctx);
        var (activity, court1, _, rsvps) = await SeedActivityWithCourtsAndPlayersAsync(ctx, orgId: 1, playerCount: 2);

        activity.AreCourtAssignmentsLocked = true;
        await ctx.SaveChangesAsync();

        var result = await service.AssignPlayerAsync(activity.Id, rsvps[0].Id, court1.Id, 1, "admin-1");

        Assert.False(result.Success);
        Assert.Contains("locked", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AssignPlayerAsync_Fails_WhenCourtNotAllocatedToActivity()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("assign_unallocated_court", organizationId: 1);
        var service = CreateService(ctx);
        var (activity, _, _, rsvps) = await SeedActivityWithCourtsAndPlayersAsync(ctx, orgId: 1, playerCount: 2);

        var unallocatedCourt = new Court { OrganizationId = 1, Name = "Court 99", Status = CourtStatus.Active };
        ctx.Courts.Add(unallocatedCourt);
        await ctx.SaveChangesAsync();

        var result = await service.AssignPlayerAsync(activity.Id, rsvps[0].Id, unallocatedCourt.Id, 1, "admin-1");

        Assert.False(result.Success);
        Assert.Contains("not allocated", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AssignPlayerAsync_Fails_WhenRsvpIsCancelledOrWaitlisted()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("assign_invalid_rsvp", organizationId: 1);
        var service = CreateService(ctx);
        var (activity, court1, _, rsvps) = await SeedActivityWithCourtsAndPlayersAsync(ctx, orgId: 1, playerCount: 2);

        rsvps[0].Status = RsvpStatus.Waitlisted;
        await ctx.SaveChangesAsync();

        var result = await service.AssignPlayerAsync(activity.Id, rsvps[0].Id, court1.Id, 1, "admin-1");

        Assert.False(result.Success);
        Assert.Contains("Only confirmed players", result.Message);
    }

    [Fact]
    public async Task MovePlayerAsync_MovesPlayerToAnotherCourt()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("move_court", organizationId: 1);
        var service = CreateService(ctx);
        var (activity, court1, court2, rsvps) = await SeedActivityWithCourtsAndPlayersAsync(ctx, orgId: 1, playerCount: 2);

        await service.AssignPlayerAsync(activity.Id, rsvps[0].Id, court1.Id, 1, "admin");
        var moveResult = await service.MovePlayerAsync(activity.Id, rsvps[0].Id, court2.Id, null, "admin");

        Assert.True(moveResult.Success);
        var assignment = await ctx.ActivityCourtAssignments.FirstOrDefaultAsync(ca => ca.ActivityRsvpId == rsvps[0].Id);
        Assert.NotNull(assignment);
        Assert.Equal(court2.Id, assignment.CourtId);
    }

    [Fact]
    public async Task UnassignPlayerAsync_RemovesPlayerFromCourt()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("unassign_player", organizationId: 1);
        var service = CreateService(ctx);
        var (activity, court1, _, rsvps) = await SeedActivityWithCourtsAndPlayersAsync(ctx, orgId: 1, playerCount: 2);

        await service.AssignPlayerAsync(activity.Id, rsvps[0].Id, court1.Id, 1, "admin");
        var unassignResult = await service.UnassignPlayerAsync(activity.Id, rsvps[0].Id, "admin");

        Assert.True(unassignResult.Success);
        var assignment = await ctx.ActivityCourtAssignments.FirstOrDefaultAsync(ca => ca.ActivityRsvpId == rsvps[0].Id);
        Assert.Null(assignment);
    }

    [Fact]
    public async Task AutoAssignAsync_DistributesPlayersEvenlyAcrossCourts()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("auto_assign_even", organizationId: 1);
        var service = CreateService(ctx);
        var (activity, court1, court2, _) = await SeedActivityWithCourtsAndPlayersAsync(ctx, orgId: 1, playerCount: 6);

        var result = await service.AutoAssignAsync(activity.Id, "admin");

        Assert.True(result.Success);
        var assignments = await ctx.ActivityCourtAssignments.Where(ca => ca.ActivityId == activity.Id).ToListAsync();
        Assert.Equal(6, assignments.Count);

        var c1Count = assignments.Count(a => a.CourtId == court1.Id);
        var c2Count = assignments.Count(a => a.CourtId == court2.Id);
        Assert.Equal(3, c1Count);
        Assert.Equal(3, c2Count);
    }

    [Fact]
    public async Task SkillBasedGroupAsync_GroupsPlayersBySkillLevel()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("skill_based_grouping", organizationId: 1);
        var service = CreateService(ctx);
        var (activity, court1, court2, rsvps) = await SeedActivityWithCourtsAndPlayersAsync(ctx, orgId: 1, playerCount: 4);

        // Explicitly set skills: 2 Advanced, 2 Beginner
        var p1 = await ctx.PlayerProfiles.FirstAsync(p => p.UserId == rsvps[0].UserId);
        var p2 = await ctx.PlayerProfiles.FirstAsync(p => p.UserId == rsvps[1].UserId);
        var p3 = await ctx.PlayerProfiles.FirstAsync(p => p.UserId == rsvps[2].UserId);
        var p4 = await ctx.PlayerProfiles.FirstAsync(p => p.UserId == rsvps[3].UserId);

        p1.SkillLevel = PlayerSkillLevel.Advanced;
        p2.SkillLevel = PlayerSkillLevel.Advanced;
        p3.SkillLevel = PlayerSkillLevel.Beginner;
        p4.SkillLevel = PlayerSkillLevel.Beginner;
        await ctx.SaveChangesAsync();

        var result = await service.SkillBasedGroupAsync(activity.Id, "admin");
        Assert.True(result.Success);

        var assignments = await ctx.ActivityCourtAssignments.Where(ca => ca.ActivityId == activity.Id).ToListAsync();
        Assert.Equal(4, assignments.Count);

        // Court 1 should have Advanced players
        var court1Assignments = assignments.Where(a => a.CourtId == court1.Id).ToList();
        var court2Assignments = assignments.Where(a => a.CourtId == court2.Id).ToList();
        Assert.Equal(2, court1Assignments.Count);
        Assert.Equal(2, court2Assignments.Count);

        Assert.Contains(court1Assignments, a => a.UserId == p1.UserId);
        Assert.Contains(court1Assignments, a => a.UserId == p2.UserId);
        Assert.Contains(court2Assignments, a => a.UserId == p3.UserId);
        Assert.Contains(court2Assignments, a => a.UserId == p4.UserId);
    }

    [Fact]
    public async Task RebalanceAsync_BalancesCourts_WhenDifferenceExceedsOne()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("rebalance_courts", organizationId: 1);
        var service = CreateService(ctx);
        var (activity, court1, court2, rsvps) = await SeedActivityWithCourtsAndPlayersAsync(ctx, orgId: 1, playerCount: 4);

        // Assign all 4 players to Court 1
        for (int i = 0; i < 4; i++)
        {
            await service.AssignPlayerAsync(activity.Id, rsvps[i].Id, court1.Id, i + 1, "admin");
        }

        var rebalanceResult = await service.RebalanceAsync(activity.Id, "admin");
        Assert.True(rebalanceResult.Success);

        var assignments = await ctx.ActivityCourtAssignments.Where(ca => ca.ActivityId == activity.Id).ToListAsync();
        var c1Count = assignments.Count(a => a.CourtId == court1.Id);
        var c2Count = assignments.Count(a => a.CourtId == court2.Id);

        Assert.Equal(2, c1Count);
        Assert.Equal(2, c2Count);
    }

    [Fact]
    public async Task ClearAssignmentsAsync_RemovesAllCourtAssignments()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("clear_assignments", organizationId: 1);
        var service = CreateService(ctx);
        var (activity, court1, _, rsvps) = await SeedActivityWithCourtsAndPlayersAsync(ctx, orgId: 1, playerCount: 3);

        await service.AssignPlayerAsync(activity.Id, rsvps[0].Id, court1.Id, 1, "admin");
        await service.AssignPlayerAsync(activity.Id, rsvps[1].Id, court1.Id, 2, "admin");

        var clearResult = await service.ClearAssignmentsAsync(activity.Id, "admin");
        Assert.True(clearResult.Success);

        var count = await ctx.ActivityCourtAssignments.CountAsync(ca => ca.ActivityId == activity.Id);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SetLockAsync_LocksAndUnlocksCourtAssignments()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("toggle_lock", organizationId: 1);
        var service = CreateService(ctx);
        var (activity, court1, _, rsvps) = await SeedActivityWithCourtsAndPlayersAsync(ctx, orgId: 1, playerCount: 2);

        // Lock
        var lockResult = await service.SetLockAsync(activity.Id, true, "admin-locker");
        Assert.True(lockResult.Success);
        Assert.Contains("locked", lockResult.Message);

        var assignAttempt = await service.AssignPlayerAsync(activity.Id, rsvps[0].Id, court1.Id, 1, "admin");
        Assert.False(assignAttempt.Success);

        // Unlock
        var unlockResult = await service.SetLockAsync(activity.Id, false, "admin-unlocker");
        Assert.True(unlockResult.Success);
        Assert.Contains("unlocked", unlockResult.Message);

        var assignSuccess = await service.AssignPlayerAsync(activity.Id, rsvps[0].Id, court1.Id, 1, "admin");
        Assert.True(assignSuccess.Success);
    }

    [Fact]
    public async Task GetPlayerCourtAssignmentAsync_ReturnsAssignedCourtInfo()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("player_court_query", organizationId: 1);
        var service = CreateService(ctx);
        var (activity, court1, _, rsvps) = await SeedActivityWithCourtsAndPlayersAsync(ctx, orgId: 1, playerCount: 2);

        await service.AssignPlayerAsync(activity.Id, rsvps[0].Id, court1.Id, 1, "admin");

        var assigned = await service.GetPlayerCourtAssignmentAsync(activity.Id, rsvps[0].UserId);
        Assert.NotNull(assigned);
        Assert.Equal(court1.Name, assigned.CourtName);
        Assert.Equal(court1.Id, assigned.CourtId);

        var unassigned = await service.GetPlayerCourtAssignmentAsync(activity.Id, rsvps[1].UserId);
        Assert.Null(unassigned);
    }
}
