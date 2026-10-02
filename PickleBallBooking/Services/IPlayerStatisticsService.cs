using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public record PlayerOverallStatsDto(
    int MatchesPlayed,
    int Wins,
    int Losses,
    int Draws,
    double WinPercentage,
    int PointsScored,
    int PointsConceded,
    int PointDifferential,
    double AveragePointsScored,
    double AveragePointsConceded,
    string CurrentStreak,
    IReadOnlyList<string> RecentForm
);

public record PartnerStatsDto(
    string? PartnerUserId,
    string PartnerName,
    string? PartnerAvatarUrl,
    PlayerSkillLevel? PartnerSkillLevel,
    int MatchesTogether,
    int Wins,
    int Losses,
    int Draws,
    double WinPercentage,
    int PointsScored,
    int PointsConceded,
    int PointDifferential
);

public record OpponentStatsDto(
    string? OpponentUserId,
    string OpponentName,
    string? OpponentAvatarUrl,
    PlayerSkillLevel? OpponentSkillLevel,
    int MatchesAgainst,
    int WinsAgainst,
    int LossesAgainst,
    int DrawsAgainst,
    double WinPercentageAgainst,
    int PointsScored,
    int PointsConceded,
    int PointDifferential
);

public record MatchOpponentDto(
    string? UserId,
    string Name,
    string? AvatarUrl
);

public record PlayerMatchHistoryItemDto(
    int MatchId,
    int ActivityId,
    string ActivityName,
    RoundRobinFormat EventFormat,
    int RoundNumber,
    string? CourtName,
    DateOnly MatchDate,
    TimeSpan? EstimatedStartTime,
    DateTime? FinalizedAt,
    int PlayerScore,
    int OpponentScore,
    string ScoreDisplay,
    string Result, // "W", "L", "D"
    string? PartnerUserId,
    string? PartnerName,
    string? PartnerAvatarUrl,
    IReadOnlyList<MatchOpponentDto> Opponents,
    int PointDifferential,
    int OrganizationId,
    string? OrganizationName
);

public record PlayerStatisticsProfileDto(
    string UserId,
    string DisplayName,
    string FullName,
    string? AvatarUrl,
    PlayerSkillLevel SkillLevel,
    PlayingHand PlayingHand,
    string? Bio,
    string? Location,
    MatchHistoryPrivacyLevel PrivacyMatchHistory,
    bool CanViewHistory,
    string? PrivacyMessage,
    PlayerOverallStatsDto Overall,
    IReadOnlyList<PartnerStatsDto> Partners,
    IReadOnlyList<OpponentStatsDto> Opponents,
    IReadOnlyList<PlayerMatchHistoryItemDto> Matches,
    bool IsGuest = false
);

public interface IPlayerStatisticsService
{
    /// <summary>
    /// Gets full player statistics, partner history, opponent history, and chronological match history.
    /// Strictly filters to finalized matches and enforces match history privacy.
    /// </summary>
    /// <param name="targetUserId">The user ID of the player whose statistics are being requested.</param>
    /// <param name="viewerUserId">The user ID of the requesting user (null if unauthenticated).</param>
    /// <param name="isStaffOrAdmin">Whether the viewer has admin/staff privileges.</param>
    /// <param name="organizationId">Optional tenant filter. If null, queries all organizations.</param>
    Task<PlayerStatisticsProfileDto?> GetPlayerStatisticsAsync(
        string targetUserId,
        string? viewerUserId = null,
        bool isStaffOrAdmin = false,
        int? organizationId = null);

    /// <summary>
    /// Updates a player's match history privacy setting.
    /// </summary>
    Task<bool> UpdateMatchHistoryPrivacyAsync(string userId, MatchHistoryPrivacyLevel privacyLevel);
}
