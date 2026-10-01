using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public record SubmitScoreDto(
    int Team1Score,
    int Team2Score,
    string? ScoresJson = null,
    bool IsLiveUpdate = false,
    string? Notes = null
);

public record CorrectScoreDto(
    int Team1Score,
    int Team2Score,
    string Reason,
    string? ScoresJson = null
);

public record MatchScoreAuditDto(
    int Id,
    int? PreviousTeam1Score,
    int? PreviousTeam2Score,
    string? PreviousWinningSide,
    int NewTeam1Score,
    int NewTeam2Score,
    string? NewWinningSide,
    string Reason,
    string ChangedByUserName,
    DateTime ChangedAt
);

public record MatchScoreDetailsDto(
    int MatchId,
    int ActivityId,
    string ActivityName,
    int RoundNumber,
    string CourtName,
    MatchStatus Status,
    bool IsFinalized,
    DateTime? FinalizedAt,
    string? FinalizedByUserName,
    string Team1Player1Name,
    string? Team1Player2Name,
    string Team2Player1Name,
    string? Team2Player2Name,
    int? Team1Score,
    int? Team2Score,
    string? WinningSide,
    string? ScoresJson,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    RoundRobinFormat Format,
    ScoringType ScoringType,
    int PointsToWin,
    bool WinByTwo,
    IReadOnlyList<MatchScoreAuditDto> Audits
);

public record LiveMatchScoreDto(
    int MatchId,
    int RoundNumber,
    string CourtName,
    MatchStatus Status,
    bool IsFinalized,
    string Team1Player1Name,
    string? Team1Player2Name,
    string Team2Player1Name,
    string? Team2Player2Name,
    int? Team1Score,
    int? Team2Score,
    string? WinningSide,
    DateTime? StartedAt,
    DateTime? CompletedAt
);

public record ScoreValidationResult(
    bool IsValid,
    string? ErrorMessage = null
);

public interface IMatchScoringService
{
    ScoreValidationResult ValidateScore(
        int team1Score,
        int team2Score,
        ScoringType scoringType,
        int pointsToWin,
        bool winByTwo,
        bool isLiveUpdate = false
    );

    Task<RoundRobinResult> StartMatchAsync(int matchId, string userId);

    Task<RoundRobinResult> RecordScoreAsync(int matchId, SubmitScoreDto dto, string userId, bool isPlayerSubmission = false);

    Task<RoundRobinResult> FinalizeMatchAsync(int matchId, string adminUserId);

    Task<RoundRobinResult> CorrectFinalizedScoreAsync(int matchId, CorrectScoreDto dto, string adminUserId);

    Task<MatchScoreDetailsDto?> GetMatchDetailsAsync(int matchId);

    Task<IReadOnlyList<LiveMatchScoreDto>> GetLiveScoresAsync(int activityId);

    Task<IReadOnlyList<MatchScoreAuditDto>> GetMatchAuditsAsync(int matchId);
}
