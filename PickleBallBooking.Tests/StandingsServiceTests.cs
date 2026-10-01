using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using Xunit;

namespace PickleBallBooking.Tests;

public class StandingsServiceTests
{
    private static StandingsService CreateService(ApplicationDbContext ctx, int orgId = 1)
    {
        var tenantContext = new TenantContext { OrganizationId = orgId };
        return new StandingsService(ctx, tenantContext);
    }

    private static async Task<(Activity Activity, RoundRobinEvent Event, List<RoundRobinMatch> Matches)> SeedEventWithMatchesAsync(
        ApplicationDbContext ctx,
        int orgId = 1,
        RoundRobinFormat format = RoundRobinFormat.RotatingPartners)
    {
        var court = new Court
        {
            OrganizationId = orgId,
            Name = "Court A",
            Status = CourtStatus.Active
        };
        ctx.Courts.Add(court);

        var activity = new Activity
        {
            OrganizationId = orgId,
            Name = "Friday Night Round Robin",
            Date = DateOnly.FromDateTime(DateTime.Today),
            StartTime = TimeSpan.FromHours(18),
            EndTime = TimeSpan.FromHours(21),
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(activity);
        await ctx.SaveChangesAsync();

        var rrEvent = new RoundRobinEvent
        {
            OrganizationId = orgId,
            ActivityId = activity.Id,
            Format = format,
            NumberOfRounds = 2,
            MatchDurationMinutes = 15,
            BreakDurationMinutes = 5,
            ScoringType = ScoringType.RallyScoring,
            PointsToWin = 11,
            WinByTwo = true,
            IsLocked = true
        };
        ctx.RoundRobinEvents.Add(rrEvent);
        await ctx.SaveChangesAsync();

        // 4 participants: Alice, Bob, Charlie, Dave
        var players = new[]
        {
            ("user-1", "Alice", "Alice Champion", PlayerSkillLevel.Advanced),
            ("user-2", "Bob", "Bob Silver", PlayerSkillLevel.Intermediate),
            ("user-3", "Charlie", "Charlie Bronze", PlayerSkillLevel.Intermediate),
            ("user-4", "Dave", "Dave Player", PlayerSkillLevel.Beginner)
        };

        foreach (var (uId, name, disp, skill) in players)
        {
            ctx.Users.Add(new IdentityUser
            {
                Id = uId,
                UserName = name,
                Email = $"{name.ToLower()}@pickleball.test"
            });

            ctx.PlayerProfiles.Add(new PlayerProfile
            {
                UserId = uId,
                FirstName = name,
                LastName = "Test",
                DisplayName = disp,
                SkillLevel = skill
            });

            ctx.ActivityRsvps.Add(new ActivityRsvp
            {
                OrganizationId = orgId,
                ActivityId = activity.Id,
                UserId = uId,
                Status = RsvpStatus.Confirmed
            });
        }
        await ctx.SaveChangesAsync();

        // Match 1: Alice & Bob vs Charlie & Dave
        // Finalized score: 11 - 5 (Alice & Bob win)
        var m1 = new RoundRobinMatch
        {
            OrganizationId = orgId,
            RoundRobinEventId = rrEvent.Id,
            RoundNumber = 1,
            CourtId = court.Id,
            CourtName = court.Name,
            Team1Player1UserId = "user-1",
            Team1Player1Name = "Alice",
            Team1Player2UserId = "user-2",
            Team1Player2Name = "Bob",
            Team2Player1UserId = "user-3",
            Team2Player1Name = "Charlie",
            Team2Player2UserId = "user-4",
            Team2Player2Name = "Dave",
            Team1Score = 11,
            Team2Score = 5,
            WinningSide = "Team1",
            Status = MatchStatus.Completed,
            IsFinalized = true,
            FinalizedAt = DateTime.UtcNow.AddMinutes(-30)
        };

        // Match 2: Alice & Charlie vs Bob & Dave
        // Finalized score: 11 - 9 (Alice & Charlie win)
        var m2 = new RoundRobinMatch
        {
            OrganizationId = orgId,
            RoundRobinEventId = rrEvent.Id,
            RoundNumber = 2,
            CourtId = court.Id,
            CourtName = court.Name,
            Team1Player1UserId = "user-1",
            Team1Player1Name = "Alice",
            Team1Player2UserId = "user-3",
            Team1Player2Name = "Charlie",
            Team2Player1UserId = "user-2",
            Team2Player1Name = "Bob",
            Team2Player2UserId = "user-4",
            Team2Player2Name = "Dave",
            Team1Score = 11,
            Team2Score = 9,
            WinningSide = "Team1",
            Status = MatchStatus.Completed,
            IsFinalized = true,
            FinalizedAt = DateTime.UtcNow.AddMinutes(-10)
        };

        // Match 3: Unfinalized (In Progress)
        // Alice & Dave vs Bob & Charlie (should NOT be included in calculations)
        var m3 = new RoundRobinMatch
        {
            OrganizationId = orgId,
            RoundRobinEventId = rrEvent.Id,
            RoundNumber = 3,
            CourtId = court.Id,
            CourtName = court.Name,
            Team1Player1UserId = "user-1",
            Team1Player1Name = "Alice",
            Team1Player2UserId = "user-4",
            Team1Player2Name = "Dave",
            Team2Player1UserId = "user-2",
            Team2Player1Name = "Bob",
            Team2Player2UserId = "user-3",
            Team2Player2Name = "Charlie",
            Team1Score = 7,
            Team2Score = 8,
            Status = MatchStatus.InProgress,
            IsFinalized = false
        };

        ctx.RoundRobinMatches.AddRange(m1, m2, m3);
        await ctx.SaveChangesAsync();

        return (activity, rrEvent, [m1, m2, m3]);
    }

    [Fact]
    public async Task GetEventStandingsAsync_ReturnsNull_WhenActivityNotFound()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("standings_not_found", 1);
        var service = CreateService(ctx);

        var result = await service.GetEventStandingsAsync(999);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetEventStandingsAsync_ComputesAccurateMetricsFromFinalizedMatchesOnly()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("standings_metrics", 1);
        var service = CreateService(ctx);
        var (activity, _, _) = await SeedEventWithMatchesAsync(ctx, 1);

        var result = await service.GetEventStandingsAsync(activity.Id);

        Assert.NotNull(result);
        Assert.Equal(3, result.TotalMatches);
        Assert.Equal(2, result.FinalizedMatches);
        Assert.Equal(1, result.InProgressMatches);
        Assert.False(result.IsEventComplete); // 1 unfinalized match remains

        var standings = result.Standings;
        Assert.Equal(4, standings.Count);

        // Expected Records from Match 1 (11-5) and Match 2 (11-9):
        // Alice:
        //   M1: Win, PF=11, PA=5
        //   M2: Win, PF=11, PA=9
        //   Total: 2 MP, 2 W, 0 L, PF=22, PA=14, Diff=+8, Win%=100.0%
        var alice = standings.First(s => s.UserId == "user-1");
        Assert.Equal(1, alice.Rank);
        Assert.Equal("Alice Champion", alice.DisplayName);
        Assert.Equal(2, alice.MatchesPlayed);
        Assert.Equal(2, alice.Wins);
        Assert.Equal(0, alice.Losses);
        Assert.Equal(22, alice.PointsScored);
        Assert.Equal(14, alice.PointsConceded);
        Assert.Equal(8, alice.PointDifferential);
        Assert.Equal(100.0, alice.WinPercentage);
        Assert.Equal(["W", "W"], alice.RecentForm);

        // Bob:
        //   M1: Win, PF=11, PA=5
        //   M2: Loss, PF=9, PA=11
        //   Total: 2 MP, 1 W, 1 L, PF=20, PA=16, Diff=+4, Win%=50.0%
        var bob = standings.First(s => s.UserId == "user-2");
        Assert.Equal(2, bob.Rank);
        Assert.Equal("Bob Silver", bob.DisplayName);
        Assert.Equal(2, bob.MatchesPlayed);
        Assert.Equal(1, bob.Wins);
        Assert.Equal(1, bob.Losses);
        Assert.Equal(20, bob.PointsScored);
        Assert.Equal(16, bob.PointsConceded);
        Assert.Equal(4, bob.PointDifferential);
        Assert.Equal(50.0, bob.WinPercentage);
        Assert.Equal(["W", "L"], bob.RecentForm);

        // Charlie:
        //   M1: Loss, PF=5, PA=11
        //   M2: Win, PF=11, PA=9
        //   Total: 2 MP, 1 W, 1 L, PF=16, PA=20, Diff=-4, Win%=50.0%
        var charlie = standings.First(s => s.UserId == "user-3");
        Assert.Equal(3, charlie.Rank);
        Assert.Equal("Charlie Bronze", charlie.DisplayName);
        Assert.Equal(2, charlie.MatchesPlayed);
        Assert.Equal(1, charlie.Wins);
        Assert.Equal(1, charlie.Losses);
        Assert.Equal(16, charlie.PointsScored);
        Assert.Equal(20, charlie.PointsConceded);
        Assert.Equal(-4, charlie.PointDifferential);
        Assert.Equal(50.0, charlie.WinPercentage);
        Assert.Equal(["L", "W"], charlie.RecentForm);

        // Dave:
        //   M1: Loss, PF=5, PA=11
        //   M2: Loss, PF=9, PA=11
        //   Total: 2 MP, 0 W, 2 L, PF=14, PA=22, Diff=-8, Win%=0.0%
        var dave = standings.First(s => s.UserId == "user-4");
        Assert.Equal(4, dave.Rank);
        Assert.Equal("Dave Player", dave.DisplayName);
        Assert.Equal(2, dave.MatchesPlayed);
        Assert.Equal(0, dave.Wins);
        Assert.Equal(2, dave.Losses);
        Assert.Equal(14, dave.PointsScored);
        Assert.Equal(22, dave.PointsConceded);
        Assert.Equal(-8, dave.PointDifferential);
        Assert.Equal(0.0, dave.WinPercentage);
        Assert.Equal(["L", "L"], dave.RecentForm);
    }

    [Fact]
    public async Task GetEventStandingsAsync_TieBreakingOrder_AppliesCorrectly()
    {
        // When two players have identical win percentage (50%),
        // Bob has Diff=+4, Charlie has Diff=-4.
        // Therefore Bob must rank higher than Charlie.
        using var ctx = TestDbContextFactory.CreateInMemory("standings_tiebreaker", 1);
        var service = CreateService(ctx);
        var (activity, _, _) = await SeedEventWithMatchesAsync(ctx, 1);

        var result = await service.GetEventStandingsAsync(activity.Id);
        Assert.NotNull(result);

        var ranks = result.Standings.Select(s => (s.Rank, s.DisplayName)).ToList();
        Assert.Equal(1, ranks[0].Rank);
        Assert.Equal("Alice Champion", ranks[0].DisplayName);

        Assert.Equal(2, ranks[1].Rank);
        Assert.Equal("Bob Silver", ranks[1].DisplayName);

        Assert.Equal(3, ranks[2].Rank);
        Assert.Equal("Charlie Bronze", ranks[2].DisplayName);

        Assert.Equal(4, ranks[3].Rank);
        Assert.Equal("Dave Player", ranks[3].DisplayName);
    }

    [Fact]
    public async Task GetTenantLeaderboardAsync_ReturnsTopPodiumAndRankings()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("leaderboard_podium", 1);
        var service = CreateService(ctx);
        await SeedEventWithMatchesAsync(ctx, 1);

        var leaderboard = await service.GetTenantLeaderboardAsync();

        Assert.NotNull(leaderboard);
        Assert.Equal(2, leaderboard.TotalFinalizedMatches);
        Assert.Equal(4, leaderboard.TotalActivePlayers);

        // Podium contains top 3
        Assert.Equal(3, leaderboard.TopPodium.Count);
        Assert.Equal("Alice Champion", leaderboard.TopPodium[0].DisplayName);
        Assert.Equal("Bob Silver", leaderboard.TopPodium[1].DisplayName);
        Assert.Equal("Charlie Bronze", leaderboard.TopPodium[2].DisplayName);

        // Complete rankings list
        Assert.Equal(4, leaderboard.Rankings.Count);
    }

    [Fact]
    public async Task GetTenantLeaderboardAsync_Filters_WorkProperly()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("leaderboard_filters", 1);
        var service = CreateService(ctx);
        await SeedEventWithMatchesAsync(ctx, 1, RoundRobinFormat.RotatingPartners);

        // Search filter
        var searchFilter = new LeaderboardFilterDto(SearchQuery: "Alice");
        var searchResult = await service.GetTenantLeaderboardAsync(searchFilter);
        Assert.Single(searchResult.Rankings);
        Assert.Equal("Alice Champion", searchResult.Rankings[0].DisplayName);

        // Format filter for Singles (no matches were seeded as Singles)
        var singlesFilter = new LeaderboardFilterDto(Format: RoundRobinFormat.Singles);
        var singlesResult = await service.GetTenantLeaderboardAsync(singlesFilter);
        Assert.Empty(singlesResult.Rankings);
        Assert.Empty(singlesResult.TopPodium);

        // Min matches filter
        var minMatchesFilter = new LeaderboardFilterDto(MinMatchesPlayed: 3);
        var minMatchesResult = await service.GetTenantLeaderboardAsync(minMatchesFilter);
        Assert.Empty(minMatchesResult.Rankings); // Everyone has 2 matches played
    }

    [Fact]
    public async Task GetPlayerStatsAsync_ReturnsAccurateIndividualRecord()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("player_stats", 1);
        var service = CreateService(ctx);
        await SeedEventWithMatchesAsync(ctx, 1);

        var aliceStats = await service.GetPlayerStatsAsync("user-1");
        Assert.NotNull(aliceStats);
        Assert.Equal("Alice Champion", aliceStats.DisplayName);
        Assert.Equal(2, aliceStats.MatchesPlayed);
        Assert.Equal(2, aliceStats.Wins);
        Assert.Equal(100.0, aliceStats.WinPercentage);

        // Non-existent player
        var missingStats = await service.GetPlayerStatsAsync("user-999");
        Assert.Null(missingStats);
    }

    [Fact]
    public async Task TenantIsolation_StandingsAndLeaderboards_AreScopedToOrganization()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("standings_tenant_iso", 1);
        var (activity, _, _) = await SeedEventWithMatchesAsync(ctx, 1);

        // Access from Tenant 2
        var serviceTenant2 = CreateService(ctx, orgId: 2);

        var eventStandings = await serviceTenant2.GetEventStandingsAsync(activity.Id);
        Assert.Null(eventStandings); // Activity 1 does not exist in Tenant 2

        var tenant2Leaderboard = await serviceTenant2.GetTenantLeaderboardAsync();
        Assert.Equal(0, tenant2Leaderboard.TotalFinalizedMatches);
        Assert.Empty(tenant2Leaderboard.Rankings);
        Assert.Empty(tenant2Leaderboard.TopPodium);
    }
}
