using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public class PlayerStatisticsService : IPlayerStatisticsService
{
    private readonly ApplicationDbContext _context;
    private readonly ICourtImageStorage _storage;
    private readonly ILogger<PlayerStatisticsService> _logger;

    public PlayerStatisticsService(
        ApplicationDbContext context,
        ICourtImageStorage storage,
        ILogger<PlayerStatisticsService> logger)
    {
        _context = context;
        _storage = storage;
        _logger = logger;
    }

    public async Task<PlayerStatisticsProfileDto?> GetPlayerStatisticsAsync(
        string targetUserId,
        string? viewerUserId = null,
        bool isStaffOrAdmin = false,
        int? organizationId = null)
    {
        if (string.IsNullOrWhiteSpace(targetUserId))
            return null;

        // 1. Retrieve target player's profile or Identity user
        var profile = await _context.PlayerProfiles
            .FirstOrDefaultAsync(p => p.UserId == targetUserId);

        var identityUser = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == targetUserId);

        if (profile == null && identityUser == null)
        {
            return null;
        }

        var displayName = !string.IsNullOrWhiteSpace(profile?.DisplayName)
            ? profile.DisplayName
            : (!string.IsNullOrWhiteSpace(profile?.FirstName)
                ? $"{profile.FirstName} {profile.LastName}".Trim()
                : (identityUser?.UserName ?? "Player"));

        var fullName = (!string.IsNullOrWhiteSpace(profile?.FirstName) && !string.IsNullOrWhiteSpace(profile?.LastName))
            ? $"{profile.FirstName} {profile.LastName}".Trim()
            : displayName;

        var avatarUrl = _storage.GetPublicUrl(profile?.AvatarPath);
        var skillLevel = profile?.SkillLevel ?? PlayerSkillLevel.Beginner;
        var playingHand = profile?.PlayingHand ?? PlayingHand.Right;
        var bio = profile?.Bio;
        var location = profile?.Location;
        var privacyLevel = profile?.PrivacyMatchHistory ?? MatchHistoryPrivacyLevel.Public;
        var isGuest = profile?.IsGuest ?? false;

        // 2. Evaluate Privacy Access
        bool isSelf = !string.IsNullOrEmpty(viewerUserId) &&
                      string.Equals(viewerUserId, targetUserId, StringComparison.OrdinalIgnoreCase);

        bool canViewHistory = isStaffOrAdmin || isSelf || privacyLevel switch
        {
            MatchHistoryPrivacyLevel.Public => true,
            MatchHistoryPrivacyLevel.FollowersOnly => !string.IsNullOrEmpty(viewerUserId),
            MatchHistoryPrivacyLevel.Private => false,
            _ => true
        };

        if (!canViewHistory)
        {
            string privacyNotice = privacyLevel switch
            {
                MatchHistoryPrivacyLevel.FollowersOnly =>
                    "This player's match history and statistics are only visible to registered members. Please sign in to view.",
                _ => "This player has set their match history and statistics to private."
            };

            var emptyOverall = new PlayerOverallStatsDto(
                MatchesPlayed: 0,
                Wins: 0,
                Losses: 0,
                Draws: 0,
                WinPercentage: 0,
                PointsScored: 0,
                PointsConceded: 0,
                PointDifferential: 0,
                AveragePointsScored: 0,
                AveragePointsConceded: 0,
                CurrentStreak: "-",
                RecentForm: []
            );

            return new PlayerStatisticsProfileDto(
                UserId: targetUserId,
                DisplayName: displayName,
                FullName: fullName,
                AvatarUrl: avatarUrl,
                SkillLevel: skillLevel,
                PlayingHand: playingHand,
                Bio: bio,
                Location: location,
                PrivacyMatchHistory: privacyLevel,
                CanViewHistory: false,
                PrivacyMessage: privacyNotice,
                Overall: emptyOverall,
                Partners: [],
                Opponents: [],
                Matches: [],
                IsGuest: isGuest
            );
        }

        // 3. Query Finalized Matches strictly
        var query = _context.RoundRobinMatches
            .IgnoreQueryFilters()
            .Include(m => m.RoundRobinEvent)
                .ThenInclude(e => e!.Activity)
            .Where(m => m.IsFinalized && m.Status != MatchStatus.Cancelled);

        if (organizationId.HasValue)
        {
            query = query.Where(m => m.OrganizationId == organizationId.Value);
        }

        var rawMatches = await query
            .Where(m => m.Team1Player1UserId == targetUserId
                     || m.Team1Player2UserId == targetUserId
                     || m.Team2Player1UserId == targetUserId
                     || m.Team2Player2UserId == targetUserId)
            .ToListAsync();

        // 4. Batch fetch related metadata (other profiles and organization names)
        var otherUserIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orgIds = new HashSet<int>();

        foreach (var m in rawMatches)
        {
            orgIds.Add(m.OrganizationId);
            if (!string.IsNullOrEmpty(m.Team1Player1UserId) && m.Team1Player1UserId != targetUserId) otherUserIds.Add(m.Team1Player1UserId);
            if (!string.IsNullOrEmpty(m.Team1Player2UserId) && m.Team1Player2UserId != targetUserId) otherUserIds.Add(m.Team1Player2UserId);
            if (!string.IsNullOrEmpty(m.Team2Player1UserId) && m.Team2Player1UserId != targetUserId) otherUserIds.Add(m.Team2Player1UserId);
            if (!string.IsNullOrEmpty(m.Team2Player2UserId) && m.Team2Player2UserId != targetUserId) otherUserIds.Add(m.Team2Player2UserId);
        }

        var otherProfiles = await _context.PlayerProfiles
            .Where(p => otherUserIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, StringComparer.OrdinalIgnoreCase);

        var orgNames = await _context.Organizations
            .Where(o => orgIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.Name);

        // 5. Build chronological match items
        var matchItems = new List<PlayerMatchHistoryItemDto>();

        foreach (var m in rawMatches)
        {
            bool isTeam1 = (m.Team1Player1UserId == targetUserId || m.Team1Player2UserId == targetUserId);

            int playerScore = isTeam1 ? (m.Team1Score ?? 0) : (m.Team2Score ?? 0);
            int opponentScore = isTeam1 ? (m.Team2Score ?? 0) : (m.Team1Score ?? 0);

            string result;
            if (!string.IsNullOrEmpty(m.WinningSide))
            {
                if (string.Equals(m.WinningSide, "Draw", StringComparison.OrdinalIgnoreCase))
                {
                    result = "D";
                }
                else if (isTeam1 && string.Equals(m.WinningSide, "Team1", StringComparison.OrdinalIgnoreCase))
                {
                    result = "W";
                }
                else if (!isTeam1 && string.Equals(m.WinningSide, "Team2", StringComparison.OrdinalIgnoreCase))
                {
                    result = "W";
                }
                else
                {
                    result = "L";
                }
            }
            else
            {
                if (playerScore > opponentScore) result = "W";
                else if (playerScore < opponentScore) result = "L";
                else result = "D";
            }

            // Identify partner
            string? partnerUserId = null;
            string? partnerName = null;
            string? partnerAvatarUrl = null;

            if (isTeam1)
            {
                if (m.Team1Player1UserId == targetUserId)
                {
                    partnerUserId = m.Team1Player2UserId;
                    partnerName = m.Team1Player2Name;
                }
                else
                {
                    partnerUserId = m.Team1Player1UserId;
                    partnerName = m.Team1Player1Name;
                }
            }
            else
            {
                if (m.Team2Player1UserId == targetUserId)
                {
                    partnerUserId = m.Team2Player2UserId;
                    partnerName = m.Team2Player2Name;
                }
                else
                {
                    partnerUserId = m.Team2Player1UserId;
                    partnerName = m.Team2Player1Name;
                }
            }

            if (!string.IsNullOrEmpty(partnerUserId) && otherProfiles.TryGetValue(partnerUserId, out var pProf))
            {
                partnerAvatarUrl = _storage.GetPublicUrl(pProf.AvatarPath);
                if (!string.IsNullOrWhiteSpace(pProf.DisplayName))
                    partnerName = pProf.DisplayName;
            }

            // Identify opponents
            var opponents = new List<MatchOpponentDto>();
            void AddOpponent(string? uid, string name)
            {
                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(uid)) return;
                string oppName = name;
                string? oppAvatar = null;
                if (!string.IsNullOrEmpty(uid) && otherProfiles.TryGetValue(uid, out var oProf))
                {
                    oppAvatar = _storage.GetPublicUrl(oProf.AvatarPath);
                    if (!string.IsNullOrWhiteSpace(oProf.DisplayName))
                        oppName = oProf.DisplayName;
                }
                opponents.Add(new MatchOpponentDto(uid, string.IsNullOrWhiteSpace(oppName) ? "Opponent" : oppName, oppAvatar));
            }

            if (isTeam1)
            {
                AddOpponent(m.Team2Player1UserId, m.Team2Player1Name);
                if (!string.IsNullOrEmpty(m.Team2Player2Name) || !string.IsNullOrEmpty(m.Team2Player2UserId))
                    AddOpponent(m.Team2Player2UserId, m.Team2Player2Name ?? "");
            }
            else
            {
                AddOpponent(m.Team1Player1UserId, m.Team1Player1Name);
                if (!string.IsNullOrEmpty(m.Team1Player2Name) || !string.IsNullOrEmpty(m.Team1Player2UserId))
                    AddOpponent(m.Team1Player2UserId, m.Team1Player2Name ?? "");
            }

            var activity = m.RoundRobinEvent?.Activity;
            var matchDate = activity?.Date ?? DateOnly.FromDateTime(m.FinalizedAt ?? DateTime.UtcNow);
            var activityName = activity?.Name ?? "Round Robin Event";
            var format = m.RoundRobinEvent?.Format ?? RoundRobinFormat.RotatingPartners;
            var orgName = orgNames.TryGetValue(m.OrganizationId, out var oName) ? oName : null;

            matchItems.Add(new PlayerMatchHistoryItemDto(
                MatchId: m.Id,
                ActivityId: m.RoundRobinEvent?.ActivityId ?? 0,
                ActivityName: activityName,
                EventFormat: format,
                RoundNumber: m.RoundNumber,
                CourtName: m.CourtName,
                MatchDate: matchDate,
                EstimatedStartTime: m.EstimatedStartTime,
                FinalizedAt: m.FinalizedAt,
                PlayerScore: playerScore,
                OpponentScore: opponentScore,
                ScoreDisplay: $"{playerScore} - {opponentScore}",
                Result: result,
                PartnerUserId: partnerUserId,
                PartnerName: partnerName,
                PartnerAvatarUrl: partnerAvatarUrl,
                Opponents: opponents,
                PointDifferential: playerScore - opponentScore,
                OrganizationId: m.OrganizationId,
                OrganizationName: orgName
            ));
        }

        // Sort descending: newest matches first
        matchItems = matchItems
            .OrderByDescending(m => m.MatchDate)
            .ThenByDescending(m => m.EstimatedStartTime ?? TimeSpan.Zero)
            .ThenByDescending(m => m.FinalizedAt ?? DateTime.MinValue)
            .ThenByDescending(m => m.RoundNumber)
            .ThenByDescending(m => m.MatchId)
            .ToList();

        // 6. Compute Overall Stats
        int matchesPlayed = matchItems.Count;
        int wins = matchItems.Count(m => m.Result == "W");
        int losses = matchItems.Count(m => m.Result == "L");
        int draws = matchItems.Count(m => m.Result == "D");
        double winPercentage = matchesPlayed > 0 ? Math.Round((double)wins / matchesPlayed * 100.0, 1) : 0;
        int pointsScored = matchItems.Sum(m => m.PlayerScore);
        int pointsConceded = matchItems.Sum(m => m.OpponentScore);
        int pointDifferential = pointsScored - pointsConceded;
        double avgScored = matchesPlayed > 0 ? Math.Round((double)pointsScored / matchesPlayed, 1) : 0;
        double avgConceded = matchesPlayed > 0 ? Math.Round((double)pointsConceded / matchesPlayed, 1) : 0;

        // Recent Form (last up to 5 matches, ordered chronologically or newest)
        var recentForm = matchItems.Take(5).Select(m => m.Result).ToList();

        // Current Streak calculation
        string streak = "-";
        if (matchItems.Count > 0)
        {
            var firstResult = matchItems[0].Result;
            int count = 0;
            foreach (var m in matchItems)
            {
                if (m.Result == firstResult) count++;
                else break;
            }
            streak = $"{firstResult}{count}";
        }

        var overallStats = new PlayerOverallStatsDto(
            MatchesPlayed: matchesPlayed,
            Wins: wins,
            Losses: losses,
            Draws: draws,
            WinPercentage: winPercentage,
            PointsScored: pointsScored,
            PointsConceded: pointsConceded,
            PointDifferential: pointDifferential,
            AveragePointsScored: avgScored,
            AveragePointsConceded: avgConceded,
            CurrentStreak: streak,
            RecentForm: recentForm
        );

        // 7. Compute Partner History
        var partnerGroups = matchItems
            .Where(m => !string.IsNullOrEmpty(m.PartnerName) || !string.IsNullOrEmpty(m.PartnerUserId))
            .GroupBy(m => !string.IsNullOrEmpty(m.PartnerUserId) ? m.PartnerUserId : m.PartnerName!);

        var partnerList = new List<PartnerStatsDto>();
        foreach (var group in partnerGroups)
        {
            var sample = group.First();
            int mTogether = group.Count();
            int pWins = group.Count(g => g.Result == "W");
            int pLosses = group.Count(g => g.Result == "L");
            int pDraws = group.Count(g => g.Result == "D");
            double pWinPct = mTogether > 0 ? Math.Round((double)pWins / mTogether * 100.0, 1) : 0;
            int pPF = group.Sum(g => g.PlayerScore);
            int pPA = group.Sum(g => g.OpponentScore);

            PlayerSkillLevel? partnerSkill = null;
            if (!string.IsNullOrEmpty(sample.PartnerUserId) && otherProfiles.TryGetValue(sample.PartnerUserId, out var prof))
            {
                partnerSkill = prof.SkillLevel;
            }

            partnerList.Add(new PartnerStatsDto(
                PartnerUserId: sample.PartnerUserId,
                PartnerName: sample.PartnerName ?? "Partner",
                PartnerAvatarUrl: sample.PartnerAvatarUrl,
                PartnerSkillLevel: partnerSkill,
                MatchesTogether: mTogether,
                Wins: pWins,
                Losses: pLosses,
                Draws: pDraws,
                WinPercentage: pWinPct,
                PointsScored: pPF,
                PointsConceded: pPA,
                PointDifferential: pPF - pPA
            ));
        }

        partnerList = partnerList
            .OrderByDescending(p => p.MatchesTogether)
            .ThenByDescending(p => p.WinPercentage)
            .ToList();

        // 8. Compute Opponent History
        var opponentDict = new Dictionary<string, (string? UserId, string Name, string? Avatar, PlayerSkillLevel? Skill, List<PlayerMatchHistoryItemDto> Matches)>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in matchItems)
        {
            foreach (var opp in m.Opponents)
            {
                var oppKey = !string.IsNullOrEmpty(opp.UserId) ? opp.UserId : opp.Name;
                if (!opponentDict.TryGetValue(oppKey, out var entry))
                {
                    PlayerSkillLevel? oppSkill = null;
                    if (!string.IsNullOrEmpty(opp.UserId) && otherProfiles.TryGetValue(opp.UserId, out var oProf))
                    {
                        oppSkill = oProf.SkillLevel;
                    }
                    entry = (opp.UserId, opp.Name, opp.AvatarUrl, oppSkill, new List<PlayerMatchHistoryItemDto>());
                    opponentDict[oppKey] = entry;
                }
                entry.Matches.Add(m);
            }
        }

        var opponentList = new List<OpponentStatsDto>();
        foreach (var (_, entry) in opponentDict)
        {
            int mAgainst = entry.Matches.Count;
            int oWinsAgainst = entry.Matches.Count(m => m.Result == "W"); // target player won
            int oLossesAgainst = entry.Matches.Count(m => m.Result == "L"); // opponent won
            int oDrawsAgainst = entry.Matches.Count(m => m.Result == "D");
            double oWinPct = mAgainst > 0 ? Math.Round((double)oWinsAgainst / mAgainst * 100.0, 1) : 0;
            int oPF = entry.Matches.Sum(m => m.PlayerScore);
            int oPA = entry.Matches.Sum(m => m.OpponentScore);

            opponentList.Add(new OpponentStatsDto(
                OpponentUserId: entry.UserId,
                OpponentName: entry.Name,
                OpponentAvatarUrl: entry.Avatar,
                OpponentSkillLevel: entry.Skill,
                MatchesAgainst: mAgainst,
                WinsAgainst: oWinsAgainst,
                LossesAgainst: oLossesAgainst,
                DrawsAgainst: oDrawsAgainst,
                WinPercentageAgainst: oWinPct,
                PointsScored: oPF,
                PointsConceded: oPA,
                PointDifferential: oPF - oPA
            ));
        }

        opponentList = opponentList
            .OrderByDescending(o => o.MatchesAgainst)
            .ThenBy(o => o.WinPercentageAgainst) // Toughest opponents (lowest win rate) or highest games
            .ToList();

        return new PlayerStatisticsProfileDto(
            UserId: targetUserId,
            DisplayName: displayName,
            FullName: fullName,
            AvatarUrl: avatarUrl,
            SkillLevel: skillLevel,
            PlayingHand: playingHand,
            Bio: bio,
            Location: location,
            PrivacyMatchHistory: privacyLevel,
            CanViewHistory: true,
            PrivacyMessage: null,
            Overall: overallStats,
            Partners: partnerList,
            Opponents: opponentList,
            Matches: matchItems,
            IsGuest: isGuest
        );
    }

    public async Task<bool> UpdateMatchHistoryPrivacyAsync(string userId, MatchHistoryPrivacyLevel privacyLevel)
    {
        var profile = await _context.PlayerProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile == null)
            return false;

        profile.PrivacyMatchHistory = privacyLevel;
        profile.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Updated match history privacy to {Privacy} for user {UserId}", privacyLevel, userId);
        return true;
    }
}
