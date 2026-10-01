using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public record RoundRobinConfigDto(
    RoundRobinFormat Format = RoundRobinFormat.RotatingPartners,
    int NumberOfRounds = 3,
    int MatchDurationMinutes = 15,
    int BreakDurationMinutes = 5,
    ScoringType ScoringType = ScoringType.RallyScoring,
    int PointsToWin = 11,
    bool WinByTwo = true,
    TimeSpan? StartTime = null,
    IReadOnlyList<int>? SelectedCourtIds = null
);

public record RoundRobinMatchDto(
    int MatchId,
    int RoundNumber,
    int? CourtId,
    string CourtName,
    TimeSpan? EstimatedStartTime,
    TimeSpan? EstimatedEndTime,
    MatchStatus Status,
    string Team1Player1Name,
    int? Team1Player1RsvpId,
    string? Team1Player1UserId,
    string? Team1Player2Name,
    int? Team1Player2RsvpId,
    string? Team1Player2UserId,
    string Team2Player1Name,
    int? Team2Player1RsvpId,
    string? Team2Player1UserId,
    string? Team2Player2Name,
    int? Team2Player2RsvpId,
    string? Team2Player2UserId,
    int? Team1Score,
    int? Team2Score,
    string? WinningSide
);

public record RoundRobinByeDto(
    int ByeId,
    int RoundNumber,
    int ActivityRsvpId,
    string? UserId,
    string PlayerName
);

public record RoundRobinRoundDto(
    int RoundNumber,
    TimeSpan? EstimatedStartTime,
    TimeSpan? EstimatedEndTime,
    IReadOnlyList<RoundRobinMatchDto> Matches,
    IReadOnlyList<RoundRobinByeDto> Byes
);

public record RoundRobinParticipantDto(
    int RsvpId,
    string? UserId,
    string PlayerName,
    string Email,
    PlayerSkillLevel? SkillLevel,
    bool IsCheckedIn
);

public record RoundRobinCourtDto(
    int CourtId,
    string CourtName
);

public record RoundRobinEventOverviewDto(
    int EventId,
    int ActivityId,
    string ActivityName,
    DateOnly Date,
    TimeSpan StartTime,
    TimeSpan EndTime,
    RoundRobinFormat Format,
    int NumberOfRounds,
    int MatchDurationMinutes,
    int BreakDurationMinutes,
    ScoringType ScoringType,
    int PointsToWin,
    bool WinByTwo,
    bool IsLocked,
    DateTime? LockedAt,
    IReadOnlyList<RoundRobinRoundDto> Rounds,
    IReadOnlyList<RoundRobinParticipantDto> Participants,
    IReadOnlyList<RoundRobinCourtDto> AvailableCourts,
    int TotalMatchesCount
);

public record PlayerRoundMatchDto(
    int RoundNumber,
    TimeSpan? EstimatedStartTime,
    TimeSpan? EstimatedEndTime,
    string CourtName,
    bool IsBye,
    string? PartnerName,
    IReadOnlyList<string> OpponentNames,
    MatchStatus? Status,
    int? TeamScore,
    int? OpponentScore,
    string? WinningSide
);

public record PlayerScheduleDto(
    int ActivityId,
    string ActivityName,
    string PlayerName,
    RoundRobinFormat Format,
    IReadOnlyList<PlayerRoundMatchDto> Rounds
);

public record RoundRobinResult(
    bool Success,
    string Message
);

/// <summary>
/// Phase 40: Round Robin schedule generation and management service.
/// </summary>
public interface IRoundRobinService
{
    Task<RoundRobinEventOverviewDto?> GetEventOverviewAsync(int activityId);

    Task<RoundRobinResult> GenerateScheduleAsync(int activityId, RoundRobinConfigDto config, string adminUserId);

    Task<RoundRobinResult> ClearScheduleAsync(int activityId, string adminUserId);

    Task<RoundRobinResult> ToggleLockAsync(int activityId, bool isLocked, string adminUserId);

    Task<RoundRobinResult> SwapMatchPlayersAsync(int matchId, string slotA, string slotB, string adminUserId);

    Task<RoundRobinResult> UpdateMatchCourtAsync(int matchId, int newCourtId, string adminUserId);

    Task<PlayerScheduleDto?> GetPlayerScheduleAsync(int activityId, string userId);
}
