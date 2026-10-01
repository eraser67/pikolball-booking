using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public class StandingsService : IStandingsService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;

    public StandingsService(ApplicationDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<EventStandingsDto?> GetEventStandingsAsync(int activityId)
    {
        var orgId = _tenantContext.OrganizationId;
        var rrEvent = await _context.RoundRobinEvents
            .Include(e => e.Activity)
            .Include(e => e.Matches)
            .FirstOrDefaultAsync(e => e.ActivityId == activityId && e.OrganizationId == orgId);

        if (rrEvent == null || rrEvent.Activity == null)
        {
            return null;
        }

        var activity = rrEvent.Activity;
        var matches = rrEvent.Matches.ToList();
        var finalizedMatches = matches.Where(m => m.IsFinalized).ToList();

        // Load participants to ensure every registered player appears on the standings table
        var rsvps = await _context.ActivityRsvps
            .Where(r => r.ActivityId == activityId && r.OrganizationId == orgId &&
                        (r.Status == RsvpStatus.Confirmed || r.Status == RsvpStatus.CheckedIn))
            .ToListAsync();

        var userIds = rsvps.Select(r => r.UserId).Distinct().ToList();
        var profiles = await _context.PlayerProfiles
            .Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId);

        var users = await _context.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.UserName ?? u.Email ?? "Player");

        // Player Stats accumulator keyed by identifier (UserId or Normalized Name)
        var statsDict = new Dictionary<string, PlayerAccumulator>(StringComparer.OrdinalIgnoreCase);

        // Pre-populate with all confirmed/checked-in participants
        foreach (var r in rsvps)
        {
            var pProfile = profiles.TryGetValue(r.UserId, out var p) ? p : null;
            var userName = users.TryGetValue(r.UserId, out var u) ? u : $"Player #{r.Id}";
            var displayName = !string.IsNullOrWhiteSpace(pProfile?.DisplayName)
                ? pProfile.DisplayName
                : (!string.IsNullOrWhiteSpace(pProfile?.FirstName) ? $"{pProfile.FirstName} {pProfile.LastName}".Trim() : userName);

            var key = $"user:{r.UserId}";
            if (!statsDict.ContainsKey(key))
            {
                statsDict[key] = new PlayerAccumulator
                {
                    Key = key,
                    UserId = r.UserId,
                    PlayerName = userName,
                    DisplayName = displayName,
                    SkillLevel = pProfile?.SkillLevel
                };
            }
        }

        // Process all finalized matches
        foreach (var m in finalizedMatches)
        {
            ProcessMatchForStandings(m, statsDict, profiles);
        }

        // Compute rankings
        var standings = CalculateRankings(statsDict.Values);

        var inProgressCount = matches.Count(m => m.Status == MatchStatus.InProgress && !m.IsFinalized);
        var scheduledCount = matches.Count(m => m.Status == MatchStatus.Scheduled && !m.IsFinalized);
        var isComplete = matches.Count > 0 && finalizedMatches.Count == matches.Count;

        return new EventStandingsDto(
            activity.Id,
            activity.Name,
            activity.Date,
            rrEvent.Format,
            rrEvent.ScoringType,
            matches.Count,
            finalizedMatches.Count,
            inProgressCount,
            scheduledCount,
            isComplete,
            standings
        );
    }

    public async Task<TenantLeaderboardDto> GetTenantLeaderboardAsync(LeaderboardFilterDto? filter = null)
    {
        var orgId = _tenantContext.OrganizationId;
        var query = _context.RoundRobinMatches
            .Include(m => m.RoundRobinEvent)
            .Where(m => m.OrganizationId == orgId && m.IsFinalized);

        if (filter?.Format.HasValue == true)
        {
            query = query.Where(m => m.RoundRobinEvent != null && m.RoundRobinEvent.Format == filter.Format.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter?.Timeframe))
        {
            var now = DateTime.UtcNow;
            if (filter.Timeframe.Equals("month", StringComparison.OrdinalIgnoreCase))
            {
                var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                query = query.Where(m => m.FinalizedAt >= startOfMonth || (m.CompletedAt >= startOfMonth && m.FinalizedAt == null));
            }
            else if (filter.Timeframe.Equals("year", StringComparison.OrdinalIgnoreCase))
            {
                var startOfYear = new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                query = query.Where(m => m.FinalizedAt >= startOfYear || (m.CompletedAt >= startOfYear && m.FinalizedAt == null));
            }
        }

        var finalizedMatches = await query.ToListAsync();

        // Extract user IDs to query profiles
        var playerUserIds = finalizedMatches
            .SelectMany(m => new[] { m.Team1Player1UserId, m.Team1Player2UserId, m.Team2Player1UserId, m.Team2Player2UserId })
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .Cast<string>()
            .ToList();

        var profiles = await _context.PlayerProfiles
            .Where(p => playerUserIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId);

        var statsDict = new Dictionary<string, PlayerAccumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in finalizedMatches)
        {
            ProcessMatchForStandings(m, statsDict, profiles);
        }

        var minMatches = filter?.MinMatchesPlayed ?? 1;
        var filteredAccumulators = statsDict.Values.Where(a => a.MatchesPlayed >= minMatches);

        if (!string.IsNullOrWhiteSpace(filter?.SearchQuery))
        {
            var search = filter.SearchQuery.Trim();
            filteredAccumulators = filteredAccumulators.Where(a =>
                a.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                a.PlayerName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var rankings = CalculateRankings(filteredAccumulators);
        var podium = rankings.Take(3).ToList();

        return new TenantLeaderboardDto(
            finalizedMatches.Count,
            rankings.Count,
            rankings,
            podium
        );
    }

    public async Task<PlayerStandingDto?> GetPlayerStatsAsync(string userId)
    {
        var orgId = _tenantContext.OrganizationId;
        var finalizedMatches = await _context.RoundRobinMatches
            .Where(m => m.OrganizationId == orgId && m.IsFinalized &&
                        (m.Team1Player1UserId == userId || m.Team1Player2UserId == userId ||
                         m.Team2Player1UserId == userId || m.Team2Player2UserId == userId))
            .ToListAsync();

        if (finalizedMatches.Count == 0)
        {
            return null;
        }

        var profile = await _context.PlayerProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        var userName = user?.UserName ?? user?.Email ?? "Player";
        var displayName = !string.IsNullOrWhiteSpace(profile?.DisplayName)
            ? profile.DisplayName
            : (!string.IsNullOrWhiteSpace(profile?.FirstName) ? $"{profile.FirstName} {profile.LastName}".Trim() : userName);

        var accumulator = new PlayerAccumulator
        {
            Key = $"user:{userId}",
            UserId = userId,
            PlayerName = userName,
            DisplayName = displayName,
            SkillLevel = profile?.SkillLevel
        };

        var dict = new Dictionary<string, PlayerAccumulator>(StringComparer.OrdinalIgnoreCase)
        {
            [accumulator.Key] = accumulator
        };

        var profilesDict = profile != null
            ? new Dictionary<string, PlayerProfile> { [userId] = profile }
            : new Dictionary<string, PlayerProfile>();

        foreach (var m in finalizedMatches)
        {
            ProcessMatchForStandings(m, dict, profilesDict);
        }

        var standings = CalculateRankings([accumulator]);
        return standings.FirstOrDefault();
    }

    private static void ProcessMatchForStandings(
        RoundRobinMatch m,
        Dictionary<string, PlayerAccumulator> statsDict,
        Dictionary<string, PlayerProfile> profiles)
    {
        var team1Score = m.Team1Score ?? 0;
        var team2Score = m.Team2Score ?? 0;

        string t1Result;
        string t2Result;

        if (string.Equals(m.WinningSide, "Team1", StringComparison.OrdinalIgnoreCase))
        {
            t1Result = "W";
            t2Result = "L";
        }
        else if (string.Equals(m.WinningSide, "Team2", StringComparison.OrdinalIgnoreCase))
        {
            t1Result = "L";
            t2Result = "W";
        }
        else
        {
            t1Result = "D";
            t2Result = "D";
        }

        // Process Team 1 players
        RecordPlayerParticipation(statsDict, profiles, m.Team1Player1UserId, m.Team1Player1Name, team1Score, team2Score, t1Result);
        if (!string.IsNullOrEmpty(m.Team1Player2Name) || !string.IsNullOrEmpty(m.Team1Player2UserId))
        {
            RecordPlayerParticipation(statsDict, profiles, m.Team1Player2UserId, m.Team1Player2Name ?? "Partner", team1Score, team2Score, t1Result);
        }

        // Process Team 2 players
        RecordPlayerParticipation(statsDict, profiles, m.Team2Player1UserId, m.Team2Player1Name, team2Score, team1Score, t2Result);
        if (!string.IsNullOrEmpty(m.Team2Player2Name) || !string.IsNullOrEmpty(m.Team2Player2UserId))
        {
            RecordPlayerParticipation(statsDict, profiles, m.Team2Player2UserId, m.Team2Player2Name ?? "Partner", team2Score, team1Score, t2Result);
        }
    }

    private static void RecordPlayerParticipation(
        Dictionary<string, PlayerAccumulator> statsDict,
        Dictionary<string, PlayerProfile> profiles,
        string? userId,
        string fallbackName,
        int pointsFor,
        int pointsAgainst,
        string result)
    {
        var key = !string.IsNullOrEmpty(userId) ? $"user:{userId}" : $"name:{fallbackName.Trim().ToLowerInvariant()}";

        if (!statsDict.TryGetValue(key, out var acc))
        {
            PlayerProfile? profile = null;
            if (!string.IsNullOrEmpty(userId)) profiles.TryGetValue(userId, out profile);

            var displayName = !string.IsNullOrWhiteSpace(profile?.DisplayName)
                ? profile.DisplayName
                : (!string.IsNullOrWhiteSpace(profile?.FirstName) ? $"{profile.FirstName} {profile.LastName}".Trim() : fallbackName);

            acc = new PlayerAccumulator
            {
                Key = key,
                UserId = userId,
                PlayerName = fallbackName,
                DisplayName = displayName,
                SkillLevel = profile?.SkillLevel
            };
            statsDict[key] = acc;
        }

        acc.MatchesPlayed++;
        if (result == "W") acc.Wins++;
        else if (result == "L") acc.Losses++;
        else acc.Draws++;

        acc.PointsScored += pointsFor;
        acc.PointsConceded += pointsAgainst;
        acc.RecentFormList.Add(result);
    }

    private static IReadOnlyList<PlayerStandingDto> CalculateRankings(IEnumerable<PlayerAccumulator> accumulators)
    {
        // Standard competitive ranking sort:
        // 1. Win Percentage desc
        // 2. Point Differential desc
        // 3. Points Scored desc
        // 4. Matches Played desc
        // 5. Display Name asc
        var sorted = accumulators
            .OrderByDescending(a => a.WinPercentage)
            .ThenByDescending(a => a.PointDifferential)
            .ThenByDescending(a => a.PointsScored)
            .ThenByDescending(a => a.MatchesPlayed)
            .ThenBy(a => a.DisplayName)
            .ToList();

        var result = new List<PlayerStandingDto>();
        for (int i = 0; i < sorted.Count; i++)
        {
            var a = sorted[i];
            // Last 5 results for recent form
            var recentForm = a.RecentFormList.TakeLast(5).ToList();

            result.Add(new PlayerStandingDto(
                Rank: i + 1,
                UserId: a.UserId,
                PlayerName: a.PlayerName,
                DisplayName: a.DisplayName,
                SkillLevel: a.SkillLevel,
                MatchesPlayed: a.MatchesPlayed,
                Wins: a.Wins,
                Losses: a.Losses,
                Draws: a.Draws,
                PointsScored: a.PointsScored,
                PointsConceded: a.PointsConceded,
                PointDifferential: a.PointDifferential,
                WinPercentage: a.WinPercentage,
                RecentForm: recentForm
            ));
        }

        return result;
    }

    private class PlayerAccumulator
    {
        public string Key { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public PlayerSkillLevel? SkillLevel { get; set; }
        public int MatchesPlayed { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Draws { get; set; }
        public int PointsScored { get; set; }
        public int PointsConceded { get; set; }
        public int PointDifferential => PointsScored - PointsConceded;
        public double WinPercentage => MatchesPlayed > 0 ? Math.Round((double)Wins / MatchesPlayed * 100.0, 1) : 0.0;
        public List<string> RecentFormList { get; } = [];
    }
}
