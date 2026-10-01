using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using Xunit;

namespace PickleBallBooking.Tests;

public class MatchScoringServiceTests
{
    private static MatchScoringService CreateService(ApplicationDbContext ctx, int orgId = 1)
    {
        var tenantContext = new TenantContext { OrganizationId = orgId };
        var notifService = new AppNotificationService(ctx, NullLogger<AppNotificationService>.Instance);
        var logger = NullLogger<MatchScoringService>.Instance;
        return new MatchScoringService(ctx, tenantContext, notifService, logger);
    }

    private static async Task<(Activity Activity, RoundRobinEvent Event, RoundRobinMatch Match)> SeedMatchAsync(
        ApplicationDbContext ctx,
        int orgId = 1,
        ScoringType scoringType = ScoringType.RallyScoring,
        int pointsToWin = 11,
        bool winByTwo = true)
    {
        var court = new Court
        {
            OrganizationId = orgId,
            Name = "Center Court",
            Status = CourtStatus.Active
        };
        ctx.Courts.Add(court);

        var activity = new Activity
        {
            OrganizationId = orgId,
            Name = "Tournament Play",
            Date = DateOnly.FromDateTime(DateTime.Today),
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(12),
            Status = ActivityStatus.RegistrationOpen
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
            BreakDurationMinutes = 5,
            ScoringType = scoringType,
            PointsToWin = pointsToWin,
            WinByTwo = winByTwo,
            IsLocked = true
        };
        ctx.RoundRobinEvents.Add(rrEvent);
        await ctx.SaveChangesAsync();

        var match = new RoundRobinMatch
        {
            OrganizationId = orgId,
            RoundRobinEventId = rrEvent.Id,
            RoundNumber = 1,
            CourtId = court.Id,
            CourtName = court.Name,
            Team1Player1Name = "Alice",
            Team1Player1UserId = "user-alice",
            Team1Player2Name = "Bob",
            Team1Player2UserId = "user-bob",
            Team2Player1Name = "Charlie",
            Team2Player1UserId = "user-charlie",
            Team2Player2Name = "Dave",
            Team2Player2UserId = "user-dave",
            Status = MatchStatus.Scheduled
        };
        ctx.RoundRobinMatches.Add(match);
        await ctx.SaveChangesAsync();

        return (activity, rrEvent, match);
    }

    [Fact]
    public void ValidateScore_NegativeScores_ReturnsInvalid()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("score_val_neg", 1);
        var service = CreateService(ctx);

        var result = service.ValidateScore(-1, 11, ScoringType.RallyScoring, 11, true);
        Assert.False(result.IsValid);
        Assert.Contains("negative", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateScore_LiveUpdate_AcceptsPartialScore()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("score_val_live", 1);
        var service = CreateService(ctx);

        var result = service.ValidateScore(5, 3, ScoringType.RallyScoring, 11, true, isLiveUpdate: true);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateScore_TimedScoring_AcceptsTiesAndAnyScore()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("score_val_timed", 1);
        var service = CreateService(ctx);

        var tieResult = service.ValidateScore(8, 8, ScoringType.Timed, 11, true, isLiveUpdate: false);
        Assert.True(tieResult.IsValid);

        var winResult = service.ValidateScore(14, 9, ScoringType.Timed, 11, true, isLiveUpdate: false);
        Assert.True(winResult.IsValid);
    }

    [Fact]
    public void ValidateScore_RallyScoring_RejectsTie()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("score_val_tie", 1);
        var service = CreateService(ctx);

        var result = service.ValidateScore(11, 11, ScoringType.RallyScoring, 11, true, isLiveUpdate: false);
        Assert.False(result.IsValid);
        Assert.Contains("tie", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateScore_PointsToWin_RejectsUnderMinimumPoints()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("score_val_under", 1);
        var service = CreateService(ctx);

        var result = service.ValidateScore(9, 7, ScoringType.RallyScoring, 11, true, isLiveUpdate: false);
        Assert.False(result.IsValid);
        Assert.Contains("at least 11 points", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateScore_WinByTwo_EnforcesMargin()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("score_val_margin", 1);
        var service = CreateService(ctx);

        // 11-10 has only 1 point diff
        var resultMargin1 = service.ValidateScore(11, 10, ScoringType.RallyScoring, 11, true);
        Assert.False(resultMargin1.IsValid);
        Assert.Contains("at least 2 points", resultMargin1.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // 12-10 is valid deuce win
        var resultDeuce = service.ValidateScore(12, 10, ScoringType.RallyScoring, 11, true);
        Assert.True(resultDeuce.IsValid);

        // 15-10 past pointsToWin with diff > 2 is invalid (play concludes at 12-10)
        var resultExcess = service.ValidateScore(15, 10, ScoringType.RallyScoring, 11, true);
        Assert.False(resultExcess.IsValid);
        Assert.Contains("Game concludes once a 2-point margin is achieved", resultExcess.ErrorMessage);
    }

    [Fact]
    public async Task StartMatchAsync_SetsInProgressAndStartedAt()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("score_start_match", 1);
        var service = CreateService(ctx);
        var (_, _, match) = await SeedMatchAsync(ctx, 1);

        var result = await service.StartMatchAsync(match.Id, "admin-user");
        Assert.True(result.Success);

        var updated = await ctx.RoundRobinMatches.FindAsync(match.Id);
        Assert.NotNull(updated);
        Assert.Equal(MatchStatus.InProgress, updated.Status);
        Assert.NotNull(updated.StartedAt);
    }

    [Fact]
    public async Task RecordScoreAsync_LiveUpdate_UpdatesScoreAndLeavesInProgress()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("score_record_live", 1);
        var service = CreateService(ctx);
        var (_, _, match) = await SeedMatchAsync(ctx, 1);

        var dto = new SubmitScoreDto(7, 4, null, IsLiveUpdate: true, Notes: "First half");
        var result = await service.RecordScoreAsync(match.Id, dto, "admin-user");

        Assert.True(result.Success);
        var updated = await ctx.RoundRobinMatches.FindAsync(match.Id);
        Assert.NotNull(updated);
        Assert.Equal(7, updated.Team1Score);
        Assert.Equal(4, updated.Team2Score);
        Assert.Equal(MatchStatus.InProgress, updated.Status);
        Assert.Null(updated.CompletedAt);
    }

    [Fact]
    public async Task RecordScoreAsync_FinalScore_SetsCompletedAndSendsNotifications()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("score_record_final", 1);
        var service = CreateService(ctx);
        var (_, _, match) = await SeedMatchAsync(ctx, 1);

        var dto = new SubmitScoreDto(11, 8, null, IsLiveUpdate: false, Notes: "Great match");
        var result = await service.RecordScoreAsync(match.Id, dto, "admin-user");

        Assert.True(result.Success);
        var updated = await ctx.RoundRobinMatches.FindAsync(match.Id);
        Assert.NotNull(updated);
        Assert.Equal(11, updated.Team1Score);
        Assert.Equal(8, updated.Team2Score);
        Assert.Equal("Team1", updated.WinningSide);
        Assert.Equal(MatchStatus.Completed, updated.Status);
        Assert.NotNull(updated.CompletedAt);

        // Verify in-app notifications dispatched to all 4 players
        var notifs = await ctx.AppNotifications.Where(n => n.Type == AppNotificationType.MatchScoreRecorded).ToListAsync();
        Assert.Equal(4, notifs.Count);
        Assert.Contains(notifs, n => n.UserId == "user-alice");
        Assert.Contains(notifs, n => n.UserId == "user-charlie");
    }

    [Fact]
    public async Task FinalizeMatchAsync_LocksOfficialScoreAndDispatchesNotification()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("score_finalize_match", 1);
        var service = CreateService(ctx);
        var (_, _, match) = await SeedMatchAsync(ctx, 1);

        // Record initial completed score
        var scoreDto = new SubmitScoreDto(11, 6, null, IsLiveUpdate: false);
        await service.RecordScoreAsync(match.Id, scoreDto, "referee-user");

        // Finalize match
        var finResult = await service.FinalizeMatchAsync(match.Id, "admin-user");
        Assert.True(finResult.Success);

        var updated = await ctx.RoundRobinMatches.FindAsync(match.Id);
        Assert.NotNull(updated);
        Assert.True(updated.IsFinalized);
        Assert.NotNull(updated.FinalizedAt);
        Assert.Equal("admin-user", updated.FinalizedByUserId);

        // Subsequent normal record score attempts must be rejected
        var failResult = await service.RecordScoreAsync(match.Id, new SubmitScoreDto(11, 7), "referee-user");
        Assert.False(failResult.Success);
        Assert.Contains("finalized", failResult.Message, StringComparison.OrdinalIgnoreCase);

        // Finalization notifications dispatched
        var finNotifs = await ctx.AppNotifications.Where(n => n.Type == AppNotificationType.MatchScoreFinalized).ToListAsync();
        Assert.Equal(4, finNotifs.Count);
    }

    [Fact]
    public async Task FinalizeMatchAsync_FailsWithoutRecordedScores()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("score_finalize_noscore", 1);
        var service = CreateService(ctx);
        var (_, _, match) = await SeedMatchAsync(ctx, 1);

        var result = await service.FinalizeMatchAsync(match.Id, "admin-user");
        Assert.False(result.Success);
        Assert.Contains("without recorded scores", result.Message);
    }

    [Fact]
    public async Task CorrectFinalizedScoreAsync_WithValidReason_UpdatesScoreAndWritesAuditLog()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("score_correct_audit", 1);
        var service = CreateService(ctx);
        var (_, _, match) = await SeedMatchAsync(ctx, 1);

        // Complete and finalize with 11-9
        await service.RecordScoreAsync(match.Id, new SubmitScoreDto(11, 9), "ref-1");
        await service.FinalizeMatchAsync(match.Id, "admin-1");

        // Seed admin user in db so username can be resolved
        ctx.Users.Add(new IdentityUser
        {
            Id = "admin-lead",
            UserName = "TournamentDirector",
            Email = "td@pickleball.test"
        });
        await ctx.SaveChangesAsync();

        // Perform score correction
        var correctDto = new CorrectScoreDto(9, 11, "Court referee reported inverted scores on original scoresheet");
        var corrResult = await service.CorrectFinalizedScoreAsync(match.Id, correctDto, "admin-lead");

        Assert.True(corrResult.Success);

        var updated = await ctx.RoundRobinMatches.Include(m => m.ScoreAudits).FirstOrDefaultAsync(m => m.Id == match.Id);
        Assert.NotNull(updated);
        Assert.Equal(9, updated.Team1Score);
        Assert.Equal(11, updated.Team2Score);
        Assert.Equal("Team2", updated.WinningSide);

        // Verify audit log entry
        Assert.Single(updated.ScoreAudits);
        var audit = updated.ScoreAudits.First();
        Assert.Equal(11, audit.PreviousTeam1Score);
        Assert.Equal(9, audit.PreviousTeam2Score);
        Assert.Equal("Team1", audit.PreviousWinningSide);
        Assert.Equal(9, audit.NewTeam1Score);
        Assert.Equal(11, audit.NewTeam2Score);
        Assert.Equal("Team2", audit.NewWinningSide);
        Assert.Equal("Court referee reported inverted scores on original scoresheet", audit.Reason);
        Assert.Equal("admin-lead", audit.ChangedByUserId);
        Assert.Equal("TournamentDirector", audit.ChangedByUserName);
    }

    [Fact]
    public async Task CorrectFinalizedScoreAsync_MissingReason_Fails()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("score_correct_no_reason", 1);
        var service = CreateService(ctx);
        var (_, _, match) = await SeedMatchAsync(ctx, 1);

        await service.RecordScoreAsync(match.Id, new SubmitScoreDto(11, 7), "ref-1");
        await service.FinalizeMatchAsync(match.Id, "admin-1");

        var correctDto = new CorrectScoreDto(11, 9, "   ");
        var result = await service.CorrectFinalizedScoreAsync(match.Id, correctDto, "admin-1");

        Assert.False(result.Success);
        Assert.Contains("reason is mandatory", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TenantIsolation_DifferentTenant_CannotAccessOrModifyMatch()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("score_tenant_iso", 1);
        var (_, _, match) = await SeedMatchAsync(ctx, 1);

        // Try accessing match from tenant 2
        var serviceTenant2 = CreateService(ctx, orgId: 2);

        var startResult = await serviceTenant2.StartMatchAsync(match.Id, "admin-t2");
        Assert.False(startResult.Success);
        Assert.Equal("Match not found.", startResult.Message);

        var recordResult = await serviceTenant2.RecordScoreAsync(match.Id, new SubmitScoreDto(11, 5), "admin-t2");
        Assert.False(recordResult.Success);
        Assert.Equal("Match not found.", recordResult.Message);

        var details = await serviceTenant2.GetMatchDetailsAsync(match.Id);
        Assert.Null(details);
    }
}
