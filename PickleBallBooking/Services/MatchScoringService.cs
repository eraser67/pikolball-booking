using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public class MatchScoringService : IMatchScoringService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly AppNotificationService _notificationService;
    private readonly ILogger<MatchScoringService> _logger;

    public MatchScoringService(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        AppNotificationService notificationService,
        ILogger<MatchScoringService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _notificationService = notificationService;
        _logger = logger;
    }

    public ScoreValidationResult ValidateScore(
        int team1Score,
        int team2Score,
        ScoringType scoringType,
        int pointsToWin,
        bool winByTwo,
        bool isLiveUpdate = false)
    {
        if (team1Score < 0 || team2Score < 0)
        {
            return new ScoreValidationResult(false, "Scores cannot be negative.");
        }

        // Live score updates during a game can be any non-negative point progress
        if (isLiveUpdate)
        {
            return new ScoreValidationResult(true);
        }

        if (scoringType == ScoringType.Timed)
        {
            // Timed play ends when time expires; any score (including a tie) is valid
            return new ScoreValidationResult(true);
        }

        // Standard Rally Scoring or Traditional Side-Out
        if (team1Score == team2Score)
        {
            return new ScoreValidationResult(false, "Standard match cannot end in a tie. One team must win.");
        }

        var maxScore = Math.Max(team1Score, team2Score);
        var minScore = Math.Min(team1Score, team2Score);
        var diff = maxScore - minScore;

        if (maxScore < pointsToWin)
        {
            return new ScoreValidationResult(false, $"Winning team must score at least {pointsToWin} points.");
        }

        if (winByTwo)
        {
            if (diff < 2)
            {
                return new ScoreValidationResult(false, "Winning team must win by at least 2 points.");
            }

            // Once past pointsToWin, game ends on exact 2-point difference (e.g. 12-10, not 15-10)
            if (minScore >= pointsToWin - 1 && diff > 2)
            {
                return new ScoreValidationResult(false, $"Game concludes once a 2-point margin is achieved (e.g. {minScore + 2}-{minScore}). Score difference of {diff} is invalid.");
            }
        }

        return new ScoreValidationResult(true);
    }

    public async Task<RoundRobinResult> StartMatchAsync(int matchId, string userId)
    {
        var orgId = _tenantContext.OrganizationId;
        var match = await _context.RoundRobinMatches
            .FirstOrDefaultAsync(m => m.Id == matchId && m.OrganizationId == orgId);

        if (match == null) return new RoundRobinResult(false, "Match not found.");

        if (match.IsFinalized)
        {
            return new RoundRobinResult(false, "Cannot modify a finalized match.");
        }

        match.Status = MatchStatus.InProgress;
        match.StartedAt ??= DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return new RoundRobinResult(true, "Match status marked as In Progress.");
    }

    public async Task<RoundRobinResult> RecordScoreAsync(int matchId, SubmitScoreDto dto, string userId, bool isPlayerSubmission = false)
    {
        var orgId = _tenantContext.OrganizationId;
        var match = await _context.RoundRobinMatches
            .Include(m => m.RoundRobinEvent)
            .FirstOrDefaultAsync(m => m.Id == matchId && m.OrganizationId == orgId);

        if (match == null) return new RoundRobinResult(false, "Match not found.");

        if (match.IsFinalized)
        {
            return new RoundRobinResult(false, "Match is already finalized. To modify a finalized score, use the score correction override with an explicit documented reason.");
        }

        var ev = match.RoundRobinEvent;
        var scoringType = ev?.ScoringType ?? ScoringType.RallyScoring;
        var pointsToWin = ev?.PointsToWin ?? 11;
        var winByTwo = ev?.WinByTwo ?? true;

        var validation = ValidateScore(dto.Team1Score, dto.Team2Score, scoringType, pointsToWin, winByTwo, dto.IsLiveUpdate);
        if (!validation.IsValid)
        {
            return new RoundRobinResult(false, validation.ErrorMessage ?? "Invalid match scores.");
        }

        string winningSide;
        if (dto.Team1Score > dto.Team2Score) winningSide = "Team1";
        else if (dto.Team2Score > dto.Team1Score) winningSide = "Team2";
        else winningSide = "Draw";

        match.Team1Score = dto.Team1Score;
        match.Team2Score = dto.Team2Score;
        match.WinningSide = winningSide;
        if (!string.IsNullOrEmpty(dto.ScoresJson)) match.ScoresJson = dto.ScoresJson;
        if (!string.IsNullOrEmpty(dto.Notes)) match.Notes = dto.Notes;

        if (dto.IsLiveUpdate)
        {
            match.Status = MatchStatus.InProgress;
            match.StartedAt ??= DateTime.UtcNow;
        }
        else
        {
            match.Status = MatchStatus.Completed;
            match.StartedAt ??= DateTime.UtcNow;
            match.CompletedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        // Dispatch in-app notification if completed
        if (!dto.IsLiveUpdate)
        {
            var playerUserIds = new[]
            {
                match.Team1Player1UserId,
                match.Team1Player2UserId,
                match.Team2Player1UserId,
                match.Team2Player2UserId
            }.Where(id => !string.IsNullOrEmpty(id)).Distinct().Cast<string>().ToList();

            var team1Label = !string.IsNullOrEmpty(match.Team1Player2Name)
                ? $"{match.Team1Player1Name} & {match.Team1Player2Name}"
                : match.Team1Player1Name;
            var team2Label = !string.IsNullOrEmpty(match.Team2Player2Name)
                ? $"{match.Team2Player1Name} & {match.Team2Player2Name}"
                : match.Team2Player1Name;

            foreach (var pUserId in playerUserIds)
            {
                await _notificationService.CreateAsync(
                    pUserId,
                    AppNotificationType.MatchScoreRecorded,
                    "Match Score Recorded",
                    $"Round {match.RoundNumber} score on {match.CourtName}: {team1Label} ({match.Team1Score}) vs {team2Label} ({match.Team2Score}).",
                    $"/Activities"
                );
            }
        }

        return new RoundRobinResult(true, dto.IsLiveUpdate ? "Live match score updated." : "Match score submitted successfully.");
    }

    public async Task<RoundRobinResult> FinalizeMatchAsync(int matchId, string adminUserId)
    {
        var orgId = _tenantContext.OrganizationId;
        var match = await _context.RoundRobinMatches
            .FirstOrDefaultAsync(m => m.Id == matchId && m.OrganizationId == orgId);

        if (match == null) return new RoundRobinResult(false, "Match not found.");

        if (match.IsFinalized)
        {
            return new RoundRobinResult(false, "Match is already finalized.");
        }

        if (!match.Team1Score.HasValue || !match.Team2Score.HasValue)
        {
            return new RoundRobinResult(false, "Cannot finalize match without recorded scores.");
        }

        match.IsFinalized = true;
        match.FinalizedAt = DateTime.UtcNow;
        match.FinalizedByUserId = adminUserId;
        match.Status = MatchStatus.Completed;
        match.CompletedAt ??= DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Dispatch finalization alert
        var playerUserIds = new[]
        {
            match.Team1Player1UserId,
            match.Team1Player2UserId,
            match.Team2Player1UserId,
            match.Team2Player2UserId
        }.Where(id => !string.IsNullOrEmpty(id)).Distinct().Cast<string>().ToList();

        foreach (var pUserId in playerUserIds)
        {
            await _notificationService.CreateAsync(
                pUserId,
                AppNotificationType.MatchScoreFinalized,
                "Official Match Score Finalized",
                $"Round {match.RoundNumber} score on {match.CourtName} has been officially finalized: {match.Team1Score} - {match.Team2Score}.",
                $"/Activities"
            );
        }

        return new RoundRobinResult(true, "Match score has been officially finalized.");
    }

    public async Task<RoundRobinResult> CorrectFinalizedScoreAsync(int matchId, CorrectScoreDto dto, string adminUserId)
    {
        var orgId = _tenantContext.OrganizationId;
        if (string.IsNullOrWhiteSpace(dto.Reason))
        {
            return new RoundRobinResult(false, "A reason is mandatory for score corrections after finalization.");
        }

        var match = await _context.RoundRobinMatches
            .Include(m => m.RoundRobinEvent)
            .FirstOrDefaultAsync(m => m.Id == matchId && m.OrganizationId == orgId);

        if (match == null) return new RoundRobinResult(false, "Match not found.");

        var ev = match.RoundRobinEvent;
        var scoringType = ev?.ScoringType ?? ScoringType.RallyScoring;
        var pointsToWin = ev?.PointsToWin ?? 11;
        var winByTwo = ev?.WinByTwo ?? true;

        var validation = ValidateScore(dto.Team1Score, dto.Team2Score, scoringType, pointsToWin, winByTwo, isLiveUpdate: false);
        if (!validation.IsValid)
        {
            return new RoundRobinResult(false, validation.ErrorMessage ?? "Invalid corrected score.");
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == adminUserId);
        var adminName = user?.UserName ?? user?.Email ?? "Administrator";

        string newWinningSide;
        if (dto.Team1Score > dto.Team2Score) newWinningSide = "Team1";
        else if (dto.Team2Score > dto.Team1Score) newWinningSide = "Team2";
        else newWinningSide = "Draw";

        var audit = new MatchScoreAudit
        {
            OrganizationId = match.OrganizationId,
            RoundRobinMatchId = match.Id,
            PreviousTeam1Score = match.Team1Score,
            PreviousTeam2Score = match.Team2Score,
            PreviousWinningSide = match.WinningSide,
            PreviousScoresJson = match.ScoresJson,
            NewTeam1Score = dto.Team1Score,
            NewTeam2Score = dto.Team2Score,
            NewWinningSide = newWinningSide,
            NewScoresJson = dto.ScoresJson ?? match.ScoresJson,
            Reason = dto.Reason.Trim(),
            ChangedByUserId = adminUserId,
            ChangedByUserName = adminName,
            ChangedAt = DateTime.UtcNow
        };

        _context.MatchScoreAudits.Add(audit);

        match.Team1Score = dto.Team1Score;
        match.Team2Score = dto.Team2Score;
        match.WinningSide = newWinningSide;
        if (!string.IsNullOrEmpty(dto.ScoresJson)) match.ScoresJson = dto.ScoresJson;
        match.FinalizedAt = DateTime.UtcNow;
        match.FinalizedByUserId = adminUserId;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Score for Match {MatchId} corrected by {Admin} (Reason: {Reason})", matchId, adminName, dto.Reason);

        return new RoundRobinResult(true, "Finalized score corrected and logged to audit trail.");
    }

    public async Task<MatchScoreDetailsDto?> GetMatchDetailsAsync(int matchId)
    {
        var orgId = _tenantContext.OrganizationId;
        var match = await _context.RoundRobinMatches
            .Include(m => m.RoundRobinEvent)
                .ThenInclude(e => e!.Activity)
            .Include(m => m.ScoreAudits)
            .FirstOrDefaultAsync(m => m.Id == matchId && m.OrganizationId == orgId);

        if (match == null) return null;

        var ev = match.RoundRobinEvent;
        var activity = ev?.Activity;

        string? finalizedByName = null;
        if (!string.IsNullOrEmpty(match.FinalizedByUserId))
        {
            var u = await _context.Users.FirstOrDefaultAsync(x => x.Id == match.FinalizedByUserId);
            finalizedByName = u?.UserName ?? u?.Email;
        }

        var audits = match.ScoreAudits
            .OrderByDescending(a => a.ChangedAt)
            .Select(a => new MatchScoreAuditDto(
                a.Id,
                a.PreviousTeam1Score,
                a.PreviousTeam2Score,
                a.PreviousWinningSide,
                a.NewTeam1Score,
                a.NewTeam2Score,
                a.NewWinningSide,
                a.Reason,
                a.ChangedByUserName,
                a.ChangedAt
            )).ToList();

        return new MatchScoreDetailsDto(
            match.Id,
            activity?.Id ?? 0,
            activity?.Name ?? "Activity",
            match.RoundNumber,
            match.CourtName ?? "Court",
            match.Status,
            match.IsFinalized,
            match.FinalizedAt,
            finalizedByName,
            match.Team1Player1Name,
            match.Team1Player2Name,
            match.Team2Player1Name,
            match.Team2Player2Name,
            match.Team1Score,
            match.Team2Score,
            match.WinningSide,
            match.ScoresJson,
            match.StartedAt,
            match.CompletedAt,
            ev?.Format ?? RoundRobinFormat.RotatingPartners,
            ev?.ScoringType ?? ScoringType.RallyScoring,
            ev?.PointsToWin ?? 11,
            ev?.WinByTwo ?? true,
            audits
        );
    }

    public async Task<IReadOnlyList<LiveMatchScoreDto>> GetLiveScoresAsync(int activityId)
    {
        var orgId = _tenantContext.OrganizationId;
        var matches = await _context.RoundRobinMatches
            .Include(m => m.RoundRobinEvent)
            .Where(m => m.RoundRobinEvent!.ActivityId == activityId && m.OrganizationId == orgId)
            .OrderBy(m => m.RoundNumber)
            .ThenBy(m => m.CourtId)
            .ToListAsync();

        return matches.Select(m => new LiveMatchScoreDto(
            m.Id,
            m.RoundNumber,
            m.CourtName ?? "Court",
            m.Status,
            m.IsFinalized,
            m.Team1Player1Name,
            m.Team1Player2Name,
            m.Team2Player1Name,
            m.Team2Player2Name,
            m.Team1Score,
            m.Team2Score,
            m.WinningSide,
            m.StartedAt,
            m.CompletedAt
        )).ToList();
    }

    public async Task<IReadOnlyList<MatchScoreAuditDto>> GetMatchAuditsAsync(int matchId)
    {
        var orgId = _tenantContext.OrganizationId;
        var audits = await _context.MatchScoreAudits
            .Where(a => a.RoundRobinMatchId == matchId && a.OrganizationId == orgId)
            .OrderByDescending(a => a.ChangedAt)
            .ToListAsync();

        return audits.Select(a => new MatchScoreAuditDto(
            a.Id,
            a.PreviousTeam1Score,
            a.PreviousTeam2Score,
            a.PreviousWinningSide,
            a.NewTeam1Score,
            a.NewTeam2Score,
            a.NewWinningSide,
            a.Reason,
            a.ChangedByUserName,
            a.ChangedAt
        )).ToList();
    }
}
