using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using Xunit;

namespace PickleBallBooking.Tests;

public class PlayerStatisticsServiceTests
{
    private sealed class FakeCourtImageStorage : ICourtImageStorage
    {
        public Task<string> UploadAsync(int organizationId, int courtId, IFormFile file, CancellationToken ct = default) => Task.FromResult("path");
        public Task<string> UploadLogoAsync(int organizationId, IFormFile file, CancellationToken ct = default) => Task.FromResult("path");
        public Task<string> UploadHeroImageAsync(int organizationId, IFormFile file, CancellationToken ct = default) => Task.FromResult("path");
        public Task<string> UploadAvatarAsync(string userId, IFormFile file, CancellationToken ct = default) => Task.FromResult("path");
        public Task DeleteAsync(string storagePath, CancellationToken ct = default) => Task.CompletedTask;
        public string? GetPublicUrl(string? storagePath) => storagePath != null ? $"https://storage.test/{storagePath}" : null;
    }

    private static PlayerStatisticsService CreateService(ApplicationDbContext ctx)
    {
        return new PlayerStatisticsService(
            ctx,
            new FakeCourtImageStorage(),
            NullLogger<PlayerStatisticsService>.Instance
        );
    }

    private static async Task SeedTestDataAsync(ApplicationDbContext ctx, int orgId = 1)
    {
        // 1. Add Organization
        var org = new Organization
        {
            Id = orgId,
            Name = "Metro Pickleball Club",
            Slug = "metro-pickleball"
        };
        ctx.Organizations.Add(org);

        // 2. Add Users & Profiles
        var players = new[]
        {
            ("user-alice", "Alice", "Alice Ace", PlayerSkillLevel.Advanced, MatchHistoryPrivacyLevel.Public),
            ("user-bob", "Bob", "Bob Blocker", PlayerSkillLevel.Intermediate, MatchHistoryPrivacyLevel.FollowersOnly),
            ("user-charlie", "Charlie", "Charlie Champ", PlayerSkillLevel.Intermediate, MatchHistoryPrivacyLevel.Private),
            ("user-dave", "Dave", "Dave Defender", PlayerSkillLevel.Beginner, MatchHistoryPrivacyLevel.Public)
        };

        foreach (var (uId, first, disp, skill, priv) in players)
        {
            ctx.Users.Add(new IdentityUser
            {
                Id = uId,
                UserName = first,
                Email = $"{first.ToLower()}@pickleball.test"
            });

            ctx.PlayerProfiles.Add(new PlayerProfile
            {
                UserId = uId,
                FirstName = first,
                LastName = "Player",
                DisplayName = disp,
                SkillLevel = skill,
                PrivacyMatchHistory = priv,
                CreatedAt = DateTime.UtcNow
            });
        }
        await ctx.SaveChangesAsync();

        // 3. Activity & RoundRobinEvent
        var activity = new Activity
        {
            OrganizationId = orgId,
            Name = "Saturday Showdown",
            Date = new DateOnly(2026, 10, 3),
            StartTime = new TimeSpan(9, 0, 0),
            EndTime = new TimeSpan(12, 0, 0)
        };
        ctx.Activities.Add(activity);
        await ctx.SaveChangesAsync();

        var rrEvent = new RoundRobinEvent
        {
            OrganizationId = orgId,
            ActivityId = activity.Id,
            Format = RoundRobinFormat.RotatingPartners,
            NumberOfRounds = 3,
            MatchDurationMinutes = 15,
            PointsToWin = 11,
            WinByTwo = true
        };
        ctx.RoundRobinEvents.Add(rrEvent);
        await ctx.SaveChangesAsync();

        // Match 1: Alice & Bob vs Charlie & Dave -> Alice & Bob win 11 - 7 (Finalized)
        var m1 = new RoundRobinMatch
        {
            OrganizationId = orgId,
            RoundRobinEventId = rrEvent.Id,
            RoundNumber = 1,
            CourtName = "Court 1",
            EstimatedStartTime = new TimeSpan(9, 0, 0),
            Team1Player1UserId = "user-alice",
            Team1Player1Name = "Alice",
            Team1Player2UserId = "user-bob",
            Team1Player2Name = "Bob",
            Team2Player1UserId = "user-charlie",
            Team2Player1Name = "Charlie",
            Team2Player2UserId = "user-dave",
            Team2Player2Name = "Dave",
            Team1Score = 11,
            Team2Score = 7,
            WinningSide = "Team1",
            Status = MatchStatus.Completed,
            IsFinalized = true,
            FinalizedAt = DateTime.UtcNow.AddMinutes(-90)
        };

        // Match 2: Charlie & Alice vs Bob & Dave -> Charlie & Alice win 11 - 9 (Finalized)
        var m2 = new RoundRobinMatch
        {
            OrganizationId = orgId,
            RoundRobinEventId = rrEvent.Id,
            RoundNumber = 2,
            CourtName = "Court 1",
            EstimatedStartTime = new TimeSpan(9, 30, 0),
            Team1Player1UserId = "user-charlie",
            Team1Player1Name = "Charlie",
            Team1Player2UserId = "user-alice",
            Team1Player2Name = "Alice",
            Team2Player1UserId = "user-bob",
            Team2Player1Name = "Bob",
            Team2Player2UserId = "user-dave",
            Team2Player2Name = "Dave",
            Team1Score = 11,
            Team2Score = 9,
            WinningSide = "Team1",
            Status = MatchStatus.Completed,
            IsFinalized = true,
            FinalizedAt = DateTime.UtcNow.AddMinutes(-60)
        };

        // Match 3: Dave & Alice vs Bob & Charlie -> Bob & Charlie win 11 - 8 (Alice loses 8 - 11) (Finalized)
        var m3 = new RoundRobinMatch
        {
            OrganizationId = orgId,
            RoundRobinEventId = rrEvent.Id,
            RoundNumber = 3,
            CourtName = "Court 1",
            EstimatedStartTime = new TimeSpan(10, 0, 0),
            Team1Player1UserId = "user-dave",
            Team1Player1Name = "Dave",
            Team1Player2UserId = "user-alice",
            Team1Player2Name = "Alice",
            Team2Player1UserId = "user-bob",
            Team2Player1Name = "Bob",
            Team2Player2UserId = "user-charlie",
            Team2Player2Name = "Charlie",
            Team1Score = 8,
            Team2Score = 11,
            WinningSide = "Team2",
            Status = MatchStatus.Completed,
            IsFinalized = true,
            FinalizedAt = DateTime.UtcNow.AddMinutes(-30)
        };

        // Match 4: UNFINALIZED match (In Progress) -> Must NOT be included in stats
        var m4 = new RoundRobinMatch
        {
            OrganizationId = orgId,
            RoundRobinEventId = rrEvent.Id,
            RoundNumber = 4,
            CourtName = "Court 1",
            EstimatedStartTime = new TimeSpan(10, 30, 0),
            Team1Player1UserId = "user-alice",
            Team1Player1Name = "Alice",
            Team2Player1UserId = "user-bob",
            Team2Player1Name = "Bob",
            Team1Score = 5,
            Team2Score = 2,
            Status = MatchStatus.InProgress,
            IsFinalized = false
        };

        // Match 5: CANCELLED match -> Must NOT be included in stats
        var m5 = new RoundRobinMatch
        {
            OrganizationId = orgId,
            RoundRobinEventId = rrEvent.Id,
            RoundNumber = 5,
            CourtName = "Court 1",
            Team1Player1UserId = "user-alice",
            Team1Player1Name = "Alice",
            Team2Player1UserId = "user-dave",
            Team2Player1Name = "Dave",
            Team1Score = 0,
            Team2Score = 0,
            Status = MatchStatus.Cancelled,
            IsFinalized = true // cancelled even if marked finalized
        };

        ctx.RoundRobinMatches.AddRange(m1, m2, m3, m4, m5);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task GetPlayerStatisticsAsync_ReturnsNull_WhenPlayerNotFound()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("stats_notfound", 1);
        var service = CreateService(ctx);

        var result = await service.GetPlayerStatisticsAsync("non-existent-user");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetPlayerStatisticsAsync_ComputesMetrics_FromFinalizedMatchesOnly()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("stats_metrics", 1);
        var service = CreateService(ctx);
        await SeedTestDataAsync(ctx, 1);

        // Alice participated in 3 finalized matches (M1: W 11-7, M2: W 11-9, M3: L 8-11)
        // M4 (unfinalized) and M5 (cancelled) must be excluded.
        var result = await service.GetPlayerStatisticsAsync("user-alice");

        Assert.NotNull(result);
        Assert.True(result.CanViewHistory);
        Assert.Equal("user-alice", result.UserId);
        Assert.Equal("Alice Ace", result.DisplayName);

        var o = result.Overall;
        Assert.Equal(3, o.MatchesPlayed);
        Assert.Equal(2, o.Wins);
        Assert.Equal(1, o.Losses);
        Assert.Equal(0, o.Draws);
        // Win rate: 2/3 = 66.7%
        Assert.Equal(66.7, o.WinPercentage);
        // Points Scored: 11 + 11 + 8 = 30
        Assert.Equal(30, o.PointsScored);
        // Points Conceded: 7 + 9 + 11 = 27
        Assert.Equal(27, o.PointsConceded);
        // Point Differential: 30 - 27 = +3
        Assert.Equal(3, o.PointDifferential);
        // Averages: 30 / 3 = 10.0, 27 / 3 = 9.0
        Assert.Equal(10.0, o.AveragePointsScored);
        Assert.Equal(9.0, o.AveragePointsConceded);
    }

    [Fact]
    public async Task GetPlayerStatisticsAsync_ComputesCurrentStreak_AndRecentForm()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("stats_streak", 1);
        var service = CreateService(ctx);
        await SeedTestDataAsync(ctx, 1);

        var result = await service.GetPlayerStatisticsAsync("user-alice");
        Assert.NotNull(result);

        // Newest match is M3 (Round 3, 10:00 AM) where Alice lost -> Result is L
        // Previous matches: M2 (W), M1 (W)
        // Recent form: ["L", "W", "W"]
        Assert.Equal(3, result.Overall.RecentForm.Count);
        Assert.Equal("L", result.Overall.RecentForm[0]);
        Assert.Equal("W", result.Overall.RecentForm[1]);
        Assert.Equal("W", result.Overall.RecentForm[2]);

        // Current streak should be "L1" (from latest match)
        Assert.Equal("L1", result.Overall.CurrentStreak);
    }

    [Fact]
    public async Task GetPlayerStatisticsAsync_AggregatesPartnerHistory_Accurately()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("stats_partners", 1);
        var service = CreateService(ctx);
        await SeedTestDataAsync(ctx, 1);

        var result = await service.GetPlayerStatisticsAsync("user-alice");
        Assert.NotNull(result);

        // Alice partnered with:
        // Bob in M1: 1 match, 1 W, 0 L, PF 11, PA 7, Diff +4
        // Charlie in M2: 1 match, 1 W, 0 L, PF 11, PA 9, Diff +2
        // Dave in M3: 1 match, 0 W, 1 L, PF 8, PA 11, Diff -3
        Assert.Equal(3, result.Partners.Count);

        var bobPartner = result.Partners.FirstOrDefault(p => p.PartnerUserId == "user-bob");
        Assert.NotNull(bobPartner);
        Assert.Equal(1, bobPartner.MatchesTogether);
        Assert.Equal(1, bobPartner.Wins);
        Assert.Equal(0, bobPartner.Losses);
        Assert.Equal(100.0, bobPartner.WinPercentage);
        Assert.Equal(4, bobPartner.PointDifferential);

        var davePartner = result.Partners.FirstOrDefault(p => p.PartnerUserId == "user-dave");
        Assert.NotNull(davePartner);
        Assert.Equal(1, davePartner.MatchesTogether);
        Assert.Equal(0, davePartner.Wins);
        Assert.Equal(1, davePartner.Losses);
        Assert.Equal(0.0, davePartner.WinPercentage);
        Assert.Equal(-3, davePartner.PointDifferential);
    }

    [Fact]
    public async Task GetPlayerStatisticsAsync_AggregatesOpponentHistory_Accurately()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("stats_opponents", 1);
        var service = CreateService(ctx);
        await SeedTestDataAsync(ctx, 1);

        var result = await service.GetPlayerStatisticsAsync("user-alice");
        Assert.NotNull(result);

        // Alice played against:
        // In M1: Charlie & Dave (Alice Won 11-7)
        // In M2: Bob & Dave (Alice Won 11-9)
        // In M3: Bob & Charlie (Alice Lost 8-11)
        // Totals:
        // vs Bob: 2 matches (M2: W, M3: L) -> 1 W, 1 L, 50.0% Win
        // vs Charlie: 2 matches (M1: W, M3: L) -> 1 W, 1 L, 50.0% Win
        // vs Dave: 2 matches (M1: W, M2: W) -> 2 W, 0 L, 100.0% Win

        Assert.Equal(3, result.Opponents.Count);

        var vsDave = result.Opponents.FirstOrDefault(o => o.OpponentUserId == "user-dave");
        Assert.NotNull(vsDave);
        Assert.Equal(2, vsDave.MatchesAgainst);
        Assert.Equal(2, vsDave.WinsAgainst);
        Assert.Equal(0, vsDave.LossesAgainst);
        Assert.Equal(100.0, vsDave.WinPercentageAgainst);

        var vsBob = result.Opponents.FirstOrDefault(o => o.OpponentUserId == "user-bob");
        Assert.NotNull(vsBob);
        Assert.Equal(2, vsBob.MatchesAgainst);
        Assert.Equal(1, vsBob.WinsAgainst);
        Assert.Equal(1, vsBob.LossesAgainst);
        Assert.Equal(50.0, vsBob.WinPercentageAgainst);
    }

    [Fact]
    public async Task GetPlayerStatisticsAsync_SortsMatchHistoryDescendingChronologically()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("stats_match_history", 1);
        var service = CreateService(ctx);
        await SeedTestDataAsync(ctx, 1);

        var result = await service.GetPlayerStatisticsAsync("user-alice");
        Assert.NotNull(result);

        // 3 matches in history
        Assert.Equal(3, result.Matches.Count);

        // Match 0: Round 3 (10:00 AM) - Latest
        Assert.Equal(3, result.Matches[0].RoundNumber);
        Assert.Equal("L", result.Matches[0].Result);
        Assert.Equal(8, result.Matches[0].PlayerScore);
        Assert.Equal(11, result.Matches[0].OpponentScore);
        Assert.Equal("user-dave", result.Matches[0].PartnerUserId);

        // Match 1: Round 2 (9:30 AM)
        Assert.Equal(2, result.Matches[1].RoundNumber);
        Assert.Equal("W", result.Matches[1].Result);
        Assert.Equal("user-charlie", result.Matches[1].PartnerUserId);

        // Match 2: Round 1 (9:00 AM)
        Assert.Equal(1, result.Matches[2].RoundNumber);
        Assert.Equal("W", result.Matches[2].Result);
        Assert.Equal("user-bob", result.Matches[2].PartnerUserId);
    }

    [Fact]
    public async Task GetPlayerStatisticsAsync_EnforcesPrivacy_Public_AllowsAll()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("stats_priv_pub", 1);
        var service = CreateService(ctx);
        await SeedTestDataAsync(ctx, 1);

        // Alice is Public: unauthenticated stranger (viewer = null) can view
        var result = await service.GetPlayerStatisticsAsync("user-alice", viewerUserId: null);

        Assert.NotNull(result);
        Assert.True(result.CanViewHistory);
        Assert.Null(result.PrivacyMessage);
        Assert.NotEmpty(result.Matches);
        Assert.NotEmpty(result.Partners);
        Assert.NotEmpty(result.Opponents);
    }

    [Fact]
    public async Task GetPlayerStatisticsAsync_EnforcesPrivacy_FollowersOnly_RestrictsUnauthenticated()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("stats_priv_followers_unauth", 1);
        var service = CreateService(ctx);
        await SeedTestDataAsync(ctx, 1);

        // Bob is FollowersOnly: unauthenticated viewer cannot view match history
        var result = await service.GetPlayerStatisticsAsync("user-bob", viewerUserId: null);

        Assert.NotNull(result);
        Assert.False(result.CanViewHistory);
        Assert.NotNull(result.PrivacyMessage);
        Assert.Contains("registered members", result.PrivacyMessage);
        Assert.Empty(result.Matches);
        Assert.Empty(result.Partners);
        Assert.Empty(result.Opponents);
    }

    [Fact]
    public async Task GetPlayerStatisticsAsync_EnforcesPrivacy_FollowersOnly_AllowsAuthenticatedMember()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("stats_priv_followers_auth", 1);
        var service = CreateService(ctx);
        await SeedTestDataAsync(ctx, 1);

        // Bob is FollowersOnly: logged-in member Alice can view
        var result = await service.GetPlayerStatisticsAsync("user-bob", viewerUserId: "user-alice");

        Assert.NotNull(result);
        Assert.True(result.CanViewHistory);
        Assert.Null(result.PrivacyMessage);
        Assert.NotEmpty(result.Matches);
    }

    [Fact]
    public async Task GetPlayerStatisticsAsync_EnforcesPrivacy_Private_RestrictsStrangers()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("stats_priv_private_stranger", 1);
        var service = CreateService(ctx);
        await SeedTestDataAsync(ctx, 1);

        // Charlie is Private: another player (Alice) cannot view
        var result = await service.GetPlayerStatisticsAsync("user-charlie", viewerUserId: "user-alice");

        Assert.NotNull(result);
        Assert.False(result.CanViewHistory);
        Assert.Contains("private", result.PrivacyMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Matches);
        Assert.Empty(result.Partners);
        Assert.Empty(result.Opponents);
    }

    [Fact]
    public async Task GetPlayerStatisticsAsync_EnforcesPrivacy_Private_AllowsSelf()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("stats_priv_private_self", 1);
        var service = CreateService(ctx);
        await SeedTestDataAsync(ctx, 1);

        // Charlie is Private: Charlie viewing himself CAN view
        var result = await service.GetPlayerStatisticsAsync("user-charlie", viewerUserId: "user-charlie");

        Assert.NotNull(result);
        Assert.True(result.CanViewHistory);
        Assert.Null(result.PrivacyMessage);
        Assert.NotEmpty(result.Matches);
    }

    [Fact]
    public async Task GetPlayerStatisticsAsync_EnforcesPrivacy_Private_AllowsAdmin()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("stats_priv_private_admin", 1);
        var service = CreateService(ctx);
        await SeedTestDataAsync(ctx, 1);

        // Charlie is Private: Admin can view
        var result = await service.GetPlayerStatisticsAsync("user-charlie", viewerUserId: "admin-user", isStaffOrAdmin: true);

        Assert.NotNull(result);
        Assert.True(result.CanViewHistory);
        Assert.Null(result.PrivacyMessage);
        Assert.NotEmpty(result.Matches);
    }

    [Fact]
    public async Task UpdateMatchHistoryPrivacyAsync_UpdatesSuccessfully()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("stats_update_priv", 1);
        var service = CreateService(ctx);
        await SeedTestDataAsync(ctx, 1);

        // Change Alice from Public to Private
        var success = await service.UpdateMatchHistoryPrivacyAsync("user-alice", MatchHistoryPrivacyLevel.Private);
        Assert.True(success);

        var updatedProfile = await ctx.PlayerProfiles.FirstAsync(p => p.UserId == "user-alice");
        Assert.Equal(MatchHistoryPrivacyLevel.Private, updatedProfile.PrivacyMatchHistory);

        // Verify stranger can no longer view
        var stats = await service.GetPlayerStatisticsAsync("user-alice", viewerUserId: "user-bob");
        Assert.False(stats!.CanViewHistory);
    }

    [Fact]
    public async Task GetPlayerStatisticsAsync_FiltersByOrganizationId_WhenSpecified()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("stats_tenant_filter", 1);
        var service = CreateService(ctx);
        await SeedTestDataAsync(ctx, 1);

        // Query with orgId = 1 (matches exist)
        var resultOrg1 = await service.GetPlayerStatisticsAsync("user-alice", organizationId: 1);
        Assert.NotNull(resultOrg1);
        Assert.Equal(3, resultOrg1.Matches.Count);

        // Query with orgId = 999 (different venue, no matches)
        var resultOrg999 = await service.GetPlayerStatisticsAsync("user-alice", organizationId: 999);
        Assert.NotNull(resultOrg999);
        Assert.Empty(resultOrg999.Matches);
        Assert.Equal(0, resultOrg999.Overall.MatchesPlayed);
    }
}
