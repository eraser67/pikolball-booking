using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public record PlayerStandingDto(
    int Rank,
    string? UserId,
    string PlayerName,
    string DisplayName,
    PlayerSkillLevel? SkillLevel,
    int MatchesPlayed,
    int Wins,
    int Losses,
    int Draws,
    int PointsScored,
    int PointsConceded,
    int PointDifferential,
    double WinPercentage,
    IReadOnlyList<string> RecentForm
);

public record EventStandingsDto(
    int ActivityId,
    string ActivityName,
    DateOnly ActivityDate,
    RoundRobinFormat Format,
    ScoringType ScoringType,
    int TotalMatches,
    int FinalizedMatches,
    int InProgressMatches,
    int ScheduledMatches,
    bool IsEventComplete,
    IReadOnlyList<PlayerStandingDto> Standings
);

public record LeaderboardFilterDto(
    RoundRobinFormat? Format = null,
    int MinMatchesPlayed = 1,
    string? Timeframe = null, // "all", "month", "year"
    string? SearchQuery = null
);

public record TenantLeaderboardDto(
    int TotalFinalizedMatches,
    int TotalActivePlayers,
    IReadOnlyList<PlayerStandingDto> Rankings,
    IReadOnlyList<PlayerStandingDto> TopPodium
);

public interface IStandingsService
{
    /// <summary>
    /// Calculates event-specific standings for an activity from finalized round robin matches.
    /// </summary>
    Task<EventStandingsDto?> GetEventStandingsAsync(int activityId);

    /// <summary>
    /// Calculates tenant-wide competitive leaderboard rankings across all finalized matches in the organization.
    /// </summary>
    Task<TenantLeaderboardDto> GetTenantLeaderboardAsync(LeaderboardFilterDto? filter = null);

    /// <summary>
    /// Retrieves accumulated competitive statistics for a specific player across all finalized matches in the tenant.
    /// </summary>
    Task<PlayerStandingDto?> GetPlayerStatsAsync(string userId);
}
