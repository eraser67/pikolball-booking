using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using Xunit;

namespace PickleBallBooking.Tests;

public class RoundRobinServiceTests
{
    private static RoundRobinService CreateService(ApplicationDbContext ctx, int orgId = 1)
    {
        var tenantContext = new TenantContext { OrganizationId = orgId };
        var notifService = new AppNotificationService(ctx, NullLogger<AppNotificationService>.Instance);
        return new RoundRobinService(ctx, tenantContext, notifService);
    }

    private static async Task<(Activity Activity, List<Court> Courts, List<ActivityRsvp> Rsvps)> SeedActivityAsync(
        ApplicationDbContext ctx,
        int orgId = 1,
        int courtCount = 2,
        int playerCount = 4)
    {
        var courts = new List<Court>();
        for (int i = 1; i <= courtCount; i++)
        {
            var court = new Court
            {
                OrganizationId = orgId,
                Name = $"Court {i}",
                Status = CourtStatus.Active
            };
            courts.Add(court);
        }
        ctx.Courts.AddRange(courts);

        var activity = new Activity
        {
            OrganizationId = orgId,
            Name = "Round Robin Open Play",
            Date = DateOnly.FromDateTime(DateTime.Today),
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(12),
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(activity);
        await ctx.SaveChangesAsync();

        foreach (var court in courts)
        {
            ctx.ActivityCourts.Add(new ActivityCourt
            {
                OrganizationId = orgId,
                ActivityId = activity.Id,
                CourtId = court.Id
            });
        }

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

            ctx.PlayerProfiles.Add(new PlayerProfile
            {
                UserId = userId,
                FirstName = $"Player{i}",
                LastName = "Tester",
                DisplayName = $"Player {i}",
                SkillLevel = PlayerSkillLevel.Intermediate
            });
        }
        ctx.ActivityRsvps.AddRange(rsvps);
        await ctx.SaveChangesAsync();

        return (activity, courts, rsvps);
    }

    [Fact]
    public async Task GetEventOverviewAsync_ReturnsNull_WhenActivityDoesNotExist()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("rr_not_found", organizationId: 1);
        var service = CreateService(ctx);

        var result = await service.GetEventOverviewAsync(999);
        Assert.Null(result);
    }

    [Fact]
    public async Task GenerateScheduleAsync_Singles_EvenPlayers_SchedulesCorrectly()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("rr_singles_even", organizationId: 1);
        var service = CreateService(ctx);
        var (activity, courts, _) = await SeedActivityAsync(ctx, orgId: 1, courtCount: 2, playerCount: 4);

        var config = new RoundRobinConfigDto(
            Format: RoundRobinFormat.Singles,
            NumberOfRounds: 3,
            MatchDurationMinutes: 15,
            BreakDurationMinutes: 5,
            ScoringType: ScoringType.RallyScoring,
            PointsToWin: 15,
            WinByTwo: true,
            StartTime: TimeSpan.FromHours(9)
        );

        var genResult = await service.GenerateScheduleAsync(activity.Id, config, "admin");
        Assert.True(genResult.Success);

        var overview = await service.GetEventOverviewAsync(activity.Id);
        Assert.NotNull(overview);
        Assert.Equal(3, overview.NumberOfRounds); // 4 players => 3 rounds
        Assert.Equal(6, overview.TotalMatchesCount); // 2 matches per round * 3 rounds = 6
        Assert.Equal(RoundRobinFormat.Singles, overview.Format);

        var allByes = overview.Rounds.SelectMany(r => r.Byes).ToList();
        Assert.Empty(allByes);

        // Verify estimated times for round 1
        var round1 = overview.Rounds.First(r => r.RoundNumber == 1);
        Assert.Equal(2, round1.Matches.Count);
        Assert.All(round1.Matches, m =>
        {
            Assert.Equal(TimeSpan.FromHours(9), m.EstimatedStartTime);
            Assert.Equal(TimeSpan.FromHours(9).Add(TimeSpan.FromMinutes(15)), m.EstimatedEndTime);
        });

        // Verify estimated times for round 2
        var round2 = overview.Rounds.First(r => r.RoundNumber == 2);
        Assert.Equal(2, round2.Matches.Count);
        Assert.All(round2.Matches, m =>
        {
            Assert.Equal(TimeSpan.FromHours(9).Add(TimeSpan.FromMinutes(20)), m.EstimatedStartTime);
            Assert.Equal(TimeSpan.FromHours(9).Add(TimeSpan.FromMinutes(35)), m.EstimatedEndTime);
        });
    }

    [Fact]
    public async Task GenerateScheduleAsync_Singles_OddPlayers_GeneratesByes()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("rr_singles_odd", organizationId: 1);
        var service = CreateService(ctx);
        var (activity, courts, _) = await SeedActivityAsync(ctx, orgId: 1, courtCount: 2, playerCount: 5);

        var config = new RoundRobinConfigDto(
            Format: RoundRobinFormat.Singles,
            NumberOfRounds: 5,
            MatchDurationMinutes: 12,
            BreakDurationMinutes: 3,
            ScoringType: ScoringType.RallyScoring,
            PointsToWin: 11,
            WinByTwo: false
        );

        var genResult = await service.GenerateScheduleAsync(activity.Id, config, "admin");
        Assert.True(genResult.Success);

        var overview = await service.GetEventOverviewAsync(activity.Id);
        Assert.NotNull(overview);
        Assert.Equal(5, overview.NumberOfRounds);

        var allByes = overview.Rounds.SelectMany(r => r.Byes).ToList();
        Assert.Equal(5, allByes.Count); // 1 bye per round = 5 total byes

        // Verify each round has 1 bye and 2 matches
        for (int r = 1; r <= 5; r++)
        {
            var round = overview.Rounds.First(rnd => rnd.RoundNumber == r);
            Assert.Single(round.Byes);
            Assert.Equal(2, round.Matches.Count);
        }
    }

    [Fact]
    public async Task GenerateScheduleAsync_FixedPartners_PairsAndSchedules()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("rr_fixed_doubles", organizationId: 1);
        var service = CreateService(ctx);
        // 8 players = 4 teams
        var (activity, courts, _) = await SeedActivityAsync(ctx, orgId: 1, courtCount: 2, playerCount: 8);

        var config = new RoundRobinConfigDto(
            Format: RoundRobinFormat.FixedPartners,
            NumberOfRounds: 3,
            MatchDurationMinutes: 15,
            BreakDurationMinutes: 5,
            ScoringType: ScoringType.TraditionalSideOut,
            PointsToWin: 11,
            WinByTwo: true
        );

        var genResult = await service.GenerateScheduleAsync(activity.Id, config, "admin");
        Assert.True(genResult.Success);

        var overview = await service.GetEventOverviewAsync(activity.Id);
        Assert.NotNull(overview);
        Assert.Equal(3, overview.NumberOfRounds);
        Assert.Equal(6, overview.TotalMatchesCount);

        var allMatches = overview.Rounds.SelectMany(r => r.Matches).ToList();
        Assert.All(allMatches, m =>
        {
            Assert.NotNull(m.Team1Player1UserId);
            Assert.NotNull(m.Team1Player2UserId);
            Assert.NotNull(m.Team2Player1UserId);
            Assert.NotNull(m.Team2Player2UserId);
        });
    }

    [Fact]
    public async Task GenerateScheduleAsync_RotatingPartners_RotatesAndAssignsCourts()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("rr_rotating_doubles", organizationId: 1);
        var service = CreateService(ctx);
        // 8 players across 2 courts (4 players per round on 2 courts = all 8 play every round)
        var (activity, courts, _) = await SeedActivityAsync(ctx, orgId: 1, courtCount: 2, playerCount: 8);

        var config = new RoundRobinConfigDto(
            Format: RoundRobinFormat.RotatingPartners,
            NumberOfRounds: 4,
            MatchDurationMinutes: 15,
            BreakDurationMinutes: 5,
            ScoringType: ScoringType.RallyScoring,
            PointsToWin: 15,
            WinByTwo: true
        );

        var genResult = await service.GenerateScheduleAsync(activity.Id, config, "admin");
        Assert.True(genResult.Success);

        var overview = await service.GetEventOverviewAsync(activity.Id);
        Assert.NotNull(overview);
        Assert.Equal(4, overview.NumberOfRounds);
        Assert.Equal(8, overview.TotalMatchesCount);

        var allByes = overview.Rounds.SelectMany(r => r.Byes).ToList();
        Assert.Empty(allByes);

        var r1 = overview.Rounds.First(r => r.RoundNumber == 1);
        Assert.Equal(2, r1.Matches.Count);
    }

    [Fact]
    public async Task GenerateScheduleAsync_RotatingPartners_WithUnevenPlayers_GeneratesByes()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("rr_rotating_byes", organizationId: 1);
        var service = CreateService(ctx);
        // 9 players on 2 courts (8 can play per round, 1 sits out as bye)
        var (activity, courts, _) = await SeedActivityAsync(ctx, orgId: 1, courtCount: 2, playerCount: 9);

        var config = new RoundRobinConfigDto(
            Format: RoundRobinFormat.RotatingPartners,
            NumberOfRounds: 4,
            MatchDurationMinutes: 15,
            BreakDurationMinutes: 5,
            ScoringType: ScoringType.RallyScoring,
            PointsToWin: 15
        );

        var genResult = await service.GenerateScheduleAsync(activity.Id, config, "admin");
        Assert.True(genResult.Success);

        var overview = await service.GetEventOverviewAsync(activity.Id);
        Assert.NotNull(overview);
        Assert.Equal(4, overview.NumberOfRounds);

        var allByes = overview.Rounds.SelectMany(r => r.Byes).ToList();
        Assert.Equal(4, allByes.Count); // 1 bye per round for 4 rounds = 4 byes
    }

    [Fact]
    public async Task LockedSchedule_CannotBeRegeneratedOrCleared()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("rr_lock_test", organizationId: 1);
        var service = CreateService(ctx);
        var (activity, courts, _) = await SeedActivityAsync(ctx, orgId: 1, courtCount: 2, playerCount: 4);

        var config = new RoundRobinConfigDto(
            Format: RoundRobinFormat.Singles,
            NumberOfRounds: 3
        );

        var genResult = await service.GenerateScheduleAsync(activity.Id, config, "admin");
        Assert.True(genResult.Success);

        // Lock schedule
        var lockResult = await service.ToggleLockAsync(activity.Id, true, "admin");
        Assert.True(lockResult.Success);

        // Attempt regenerate -> should fail
        var regenResult = await service.GenerateScheduleAsync(activity.Id, config, "admin");
        Assert.False(regenResult.Success);

        // Attempt clear -> should fail
        var clearFailResult = await service.ClearScheduleAsync(activity.Id, "admin");
        Assert.False(clearFailResult.Success);

        // Unlock
        var unlockResult = await service.ToggleLockAsync(activity.Id, false, "admin");
        Assert.True(unlockResult.Success);

        // Clear should succeed now
        var cleared = await service.ClearScheduleAsync(activity.Id, "admin");
        Assert.True(cleared.Success);

        var overview = await service.GetEventOverviewAsync(activity.Id);
        Assert.NotNull(overview);
        Assert.Equal(0, overview.TotalMatchesCount);
    }

    [Fact]
    public async Task UpdateMatchCourtAsync_UpdatesCourtCorrectly()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("rr_court_update", organizationId: 1);
        var service = CreateService(ctx);
        var (activity, courts, _) = await SeedActivityAsync(ctx, orgId: 1, courtCount: 2, playerCount: 4);

        var config = new RoundRobinConfigDto(
            Format: RoundRobinFormat.Singles,
            NumberOfRounds: 3
        );

        await service.GenerateScheduleAsync(activity.Id, config, "admin");
        var overview = await service.GetEventOverviewAsync(activity.Id);
        var match = overview!.Rounds.First().Matches.First();

        var court2 = courts[1];
        var updateResult = await service.UpdateMatchCourtAsync(match.MatchId, court2.Id, "admin");
        Assert.True(updateResult.Success);

        var reloaded = await ctx.RoundRobinMatches.FindAsync(match.MatchId);
        Assert.NotNull(reloaded);
        Assert.Equal(court2.Id, reloaded.CourtId);
    }

    [Fact]
    public async Task GetPlayerScheduleAsync_ReturnsPlayerSpecificSchedule()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("rr_player_schedule", organizationId: 1);
        var service = CreateService(ctx);
        var (activity, courts, rsvps) = await SeedActivityAsync(ctx, orgId: 1, courtCount: 2, playerCount: 5);

        var config = new RoundRobinConfigDto(
            Format: RoundRobinFormat.Singles,
            NumberOfRounds: 5
        );

        await service.GenerateScheduleAsync(activity.Id, config, "admin");

        var p1UserId = rsvps[0].UserId;
        var p1Schedule = await service.GetPlayerScheduleAsync(activity.Id, p1UserId);
        Assert.NotNull(p1Schedule);

        Assert.Equal(5, p1Schedule.Rounds.Count); // 5 rounds total
        // Player 1 will have matches in 4 rounds and a bye in 1 round
        var playedRounds = p1Schedule.Rounds.Where(s => !s.IsBye).ToList();
        var byeRounds = p1Schedule.Rounds.Where(s => s.IsBye).ToList();

        Assert.Equal(4, playedRounds.Count);
        Assert.Single(byeRounds);
        Assert.All(playedRounds, p =>
        {
            Assert.False(string.IsNullOrEmpty(p.CourtName));
            Assert.NotEmpty(p.OpponentNames);
        });
    }

    [Fact]
    public async Task TenantIsolation_DifferentTenantCannotAccessMatches()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("rr_tenant_iso", organizationId: 1);
        var service = CreateService(ctx);
        var (activity, courts, _) = await SeedActivityAsync(ctx, orgId: 1, courtCount: 2, playerCount: 4);

        var config = new RoundRobinConfigDto(
            Format: RoundRobinFormat.Singles,
            NumberOfRounds: 3
        );

        await service.GenerateScheduleAsync(activity.Id, config, "admin");

        // Create context for Tenant 2
        using var ctxOrg2 = TestDbContextFactory.CreateInMemory("rr_tenant_iso", organizationId: 2);
        var serviceOrg2 = CreateService(ctxOrg2, orgId: 2);

        // Attempting to query activity from tenant 2 returns null because of tenant query filter
        var overview = await serviceOrg2.GetEventOverviewAsync(activity.Id);
        Assert.Null(overview);
    }
}
