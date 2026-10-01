using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public class RoundRobinService : IRoundRobinService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly AppNotificationService _notificationService;

    public RoundRobinService(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        AppNotificationService notificationService)
    {
        _context = context;
        _tenantContext = tenantContext;
        _notificationService = notificationService;
    }

    public async Task<RoundRobinEventOverviewDto?> GetEventOverviewAsync(int activityId)
    {
        var orgId = _tenantContext.OrganizationId;
        var activity = await _context.Activities
            .Include(a => a.ActivityCourts)
                .ThenInclude(ac => ac.Court)
            .FirstOrDefaultAsync(a => a.Id == activityId && a.OrganizationId == orgId);

        if (activity == null) return null;

        var rrEvent = await _context.RoundRobinEvents
            .Include(e => e.Matches)
            .Include(e => e.Byes)
            .FirstOrDefaultAsync(e => e.ActivityId == activityId && e.OrganizationId == orgId);

        // Fetch confirmed/checked-in participants
        var rsvps = await _context.ActivityRsvps
            .Where(r => r.ActivityId == activityId && r.OrganizationId == orgId &&
                        (r.Status == RsvpStatus.Confirmed || r.Status == RsvpStatus.CheckedIn))
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();

        // Resolve names via PlayerProfiles or Users
        var userIds = rsvps.Select(r => r.UserId).Distinct().ToList();
        var profiles = await _context.PlayerProfiles
            .Where(p => userIds.Contains(p.UserId))
            .ToListAsync();
        var profileNames = profiles.ToDictionary(
            p => p.UserId,
            p => !string.IsNullOrWhiteSpace(p.DisplayName) ? p.DisplayName : $"{p.FirstName} {p.LastName}".Trim()
        );

        var users = await _context.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.UserName ?? u.Email ?? "Player");

        var participants = rsvps.Select(r =>
        {
            var name = profileNames.TryGetValue(r.UserId, out var pName) && !string.IsNullOrWhiteSpace(pName)
                ? pName
                : (users.TryGetValue(r.UserId, out var uName) ? uName : $"Player #{r.Id}");

            return new RoundRobinParticipantDto(
                r.Id,
                r.UserId,
                name,
                users.TryGetValue(r.UserId, out var email) ? email : string.Empty,
                null,
                r.Status == RsvpStatus.CheckedIn
            );
        }).ToList();

        var availableCourts = activity.ActivityCourts
            .Where(ac => ac.Court != null && ac.Court.Status == CourtStatus.Active)
            .Select(ac => new RoundRobinCourtDto(ac.CourtId, ac.Court!.Name))
            .ToList();

        if (rrEvent == null)
        {
            return new RoundRobinEventOverviewDto(
                0,
                activity.Id,
                activity.Name,
                activity.Date,
                activity.StartTime,
                activity.EndTime,
                RoundRobinFormat.RotatingPartners,
                3,
                15,
                5,
                ScoringType.RallyScoring,
                11,
                true,
                false,
                null,
                [],
                participants,
                availableCourts,
                0
            );
        }

        var roundsDict = new SortedDictionary<int, List<RoundRobinMatchDto>>();
        foreach (var m in rrEvent.Matches.OrderBy(m => m.RoundNumber).ThenBy(m => m.Id))
        {
            if (!roundsDict.TryGetValue(m.RoundNumber, out var list))
            {
                list = [];
                roundsDict[m.RoundNumber] = list;
            }

            var courtName = m.CourtName ?? (availableCourts.FirstOrDefault(c => c.CourtId == m.CourtId)?.CourtName ?? "Court");
            list.Add(new RoundRobinMatchDto(
                m.Id,
                m.RoundNumber,
                m.CourtId,
                courtName,
                m.EstimatedStartTime,
                m.EstimatedEndTime,
                m.Status,
                m.Team1Player1Name,
                m.Team1Player1RsvpId,
                m.Team1Player1UserId,
                m.Team1Player2Name,
                m.Team1Player2RsvpId,
                m.Team1Player2UserId,
                m.Team2Player1Name,
                m.Team2Player1RsvpId,
                m.Team2Player1UserId,
                m.Team2Player2Name,
                m.Team2Player2RsvpId,
                m.Team2Player2UserId,
                m.Team1Score,
                m.Team2Score,
                m.WinningSide
            ));
        }

        var byesDict = new SortedDictionary<int, List<RoundRobinByeDto>>();
        foreach (var b in rrEvent.Byes.OrderBy(b => b.RoundNumber).ThenBy(b => b.Id))
        {
            if (!byesDict.TryGetValue(b.RoundNumber, out var blist))
            {
                blist = [];
                byesDict[b.RoundNumber] = blist;
            }
            blist.Add(new RoundRobinByeDto(b.Id, b.RoundNumber, b.ActivityRsvpId, b.UserId, b.PlayerName));
        }

        var allRoundNumbers = Enumerable.Range(1, rrEvent.NumberOfRounds).ToList();
        var roundDtos = allRoundNumbers.Select(rn =>
        {
            var matches = roundsDict.TryGetValue(rn, out var mList) ? mList : (IReadOnlyList<RoundRobinMatchDto>)[];
            var byes = byesDict.TryGetValue(rn, out var bList) ? bList : (IReadOnlyList<RoundRobinByeDto>)[];
            var estStart = matches.FirstOrDefault()?.EstimatedStartTime;
            var estEnd = matches.FirstOrDefault()?.EstimatedEndTime;

            return new RoundRobinRoundDto(rn, estStart, estEnd, matches, byes);
        }).ToList();

        return new RoundRobinEventOverviewDto(
            rrEvent.Id,
            activity.Id,
            activity.Name,
            activity.Date,
            activity.StartTime,
            activity.EndTime,
            rrEvent.Format,
            rrEvent.NumberOfRounds,
            rrEvent.MatchDurationMinutes,
            rrEvent.BreakDurationMinutes,
            rrEvent.ScoringType,
            rrEvent.PointsToWin,
            rrEvent.WinByTwo,
            rrEvent.IsLocked,
            rrEvent.LockedAt,
            roundDtos,
            participants,
            availableCourts,
            rrEvent.Matches.Count
        );
    }

    public async Task<RoundRobinResult> GenerateScheduleAsync(int activityId, RoundRobinConfigDto config, string adminUserId)
    {
        var orgId = _tenantContext.OrganizationId;
        var activity = await _context.Activities
            .Include(a => a.ActivityCourts)
                .ThenInclude(ac => ac.Court)
            .FirstOrDefaultAsync(a => a.Id == activityId && a.OrganizationId == orgId);

        if (activity == null)
            return new RoundRobinResult(false, "Activity not found.");

        var courts = activity.ActivityCourts
            .Where(ac => ac.Court != null && ac.Court.Status == CourtStatus.Active)
            .Select(ac => ac.Court!)
            .ToList();

        if (courts.Count == 0)
            return new RoundRobinResult(false, "Cannot generate round robin: no courts allocated to this activity.");

        var rsvps = await _context.ActivityRsvps
            .Where(r => r.ActivityId == activityId && r.OrganizationId == orgId &&
                        (r.Status == RsvpStatus.Confirmed || r.Status == RsvpStatus.CheckedIn))
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();

        if (rsvps.Count < 2)
            return new RoundRobinResult(false, "At least 2 confirmed players are required to generate a round robin schedule.");

        if (config.Format != RoundRobinFormat.Singles && rsvps.Count < 4)
            return new RoundRobinResult(false, "At least 4 confirmed players are required for doubles round robin formats.");

        // Resolve player names
        var userIds = rsvps.Select(r => r.UserId).Distinct().ToList();
        var profiles = await _context.PlayerProfiles
            .Where(p => userIds.Contains(p.UserId))
            .ToListAsync();
        var profileNames = profiles.ToDictionary(
            p => p.UserId,
            p => !string.IsNullOrWhiteSpace(p.DisplayName) ? p.DisplayName : $"{p.FirstName} {p.LastName}".Trim()
        );

        var users = await _context.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.UserName ?? u.Email ?? "Player");

        var players = rsvps.Select(r =>
        {
            var name = profileNames.TryGetValue(r.UserId, out var pName) && !string.IsNullOrWhiteSpace(pName)
                ? pName
                : (users.TryGetValue(r.UserId, out var uName) ? uName : $"Player #{r.Id}");

            return new PlayerInfo(r.Id, r.UserId, name);
        }).ToList();

        // Check if existing event exists
        var existingEvent = await _context.RoundRobinEvents
            .Include(e => e.Matches)
            .Include(e => e.Byes)
            .FirstOrDefaultAsync(e => e.ActivityId == activityId && e.OrganizationId == orgId);

        if (existingEvent != null && existingEvent.IsLocked)
            return new RoundRobinResult(false, "Round robin schedule is locked. Unlock it first before regenerating.");

        if (existingEvent != null)
        {
            _context.RoundRobinMatches.RemoveRange(existingEvent.Matches);
            _context.RoundRobinByes.RemoveRange(existingEvent.Byes);
            _context.RoundRobinEvents.Remove(existingEvent);
            await _context.SaveChangesAsync();
        }

        var effectiveOrgId = orgId ?? activity.OrganizationId;
        var rrEvent = new RoundRobinEvent
        {
            OrganizationId = effectiveOrgId,
            ActivityId = activityId,
            Format = config.Format,
            NumberOfRounds = Math.Max(1, config.NumberOfRounds),
            MatchDurationMinutes = Math.Max(5, config.MatchDurationMinutes),
            BreakDurationMinutes = Math.Max(0, config.BreakDurationMinutes),
            ScoringType = config.ScoringType,
            PointsToWin = Math.Max(1, config.PointsToWin),
            WinByTwo = config.WinByTwo,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = adminUserId
        };

        _context.RoundRobinEvents.Add(rrEvent);
        await _context.SaveChangesAsync();

        var startTime = config.StartTime ?? activity.StartTime;
        var roundDuration = TimeSpan.FromMinutes(rrEvent.MatchDurationMinutes);
        var breakDuration = TimeSpan.FromMinutes(rrEvent.BreakDurationMinutes);

        var matchesToInsert = new List<RoundRobinMatch>();
        var byesToInsert = new List<RoundRobinBye>();

        switch (config.Format)
        {
            case RoundRobinFormat.Singles:
                GenerateSinglesSchedule(rrEvent, players, courts, config.NumberOfRounds, startTime, roundDuration, breakDuration, matchesToInsert, byesToInsert);
                break;

            case RoundRobinFormat.FixedPartners:
                GenerateFixedPartnersSchedule(rrEvent, players, courts, config.NumberOfRounds, startTime, roundDuration, breakDuration, matchesToInsert, byesToInsert);
                break;

            case RoundRobinFormat.RotatingPartners:
            default:
                GenerateRotatingPartnersSchedule(rrEvent, players, courts, config.NumberOfRounds, startTime, roundDuration, breakDuration, matchesToInsert, byesToInsert);
                break;
        }

        _context.RoundRobinMatches.AddRange(matchesToInsert);
        _context.RoundRobinByes.AddRange(byesToInsert);
        await _context.SaveChangesAsync();

        // Send in-app notification to participants
        foreach (var p in players)
        {
            if (!string.IsNullOrEmpty(p.UserId))
            {
                await _notificationService.CreateAsync(
                    p.UserId,
                    AppNotificationType.ActivityRoundRobinScheduled,
                    "Round Robin Schedule Ready",
                    $"The round robin match schedule for '{activity.Name}' has been generated ({config.NumberOfRounds} rounds). View your matches now!",
                    $"/Activities"
                );
            }
        }

        return new RoundRobinResult(true, $"Generated round robin schedule with {matchesToInsert.Count} matches across {config.NumberOfRounds} rounds.");
    }

    private void GenerateSinglesSchedule(
        RoundRobinEvent rrEvent,
        List<PlayerInfo> players,
        List<Court> courts,
        int totalRounds,
        TimeSpan activityStartTime,
        TimeSpan matchDuration,
        TimeSpan breakDuration,
        List<RoundRobinMatch> matchesOut,
        List<RoundRobinBye> byesOut)
    {
        var n = players.Count;
        var isOdd = n % 2 != 0;
        var pool = new List<PlayerInfo?>(players);
        if (isOdd) pool.Add(null); // Dummy player for bye

        var poolSize = pool.Count;
        var currentStart = activityStartTime;

        for (int round = 1; round <= totalRounds; round++)
        {
            var roundEnd = currentStart.Add(matchDuration);
            var courtIndex = 0;

            for (int i = 0; i < poolSize / 2; i++)
            {
                var p1 = pool[i];
                var p2 = pool[poolSize - 1 - i];

                if (p1 == null && p2 != null)
                {
                    byesOut.Add(new RoundRobinBye
                    {
                        OrganizationId = rrEvent.OrganizationId,
                        RoundRobinEventId = rrEvent.Id,
                        RoundNumber = round,
                        ActivityRsvpId = p2.RsvpId,
                        UserId = p2.UserId,
                        PlayerName = p2.Name
                    });
                }
                else if (p2 == null && p1 != null)
                {
                    byesOut.Add(new RoundRobinBye
                    {
                        OrganizationId = rrEvent.OrganizationId,
                        RoundRobinEventId = rrEvent.Id,
                        RoundNumber = round,
                        ActivityRsvpId = p1.RsvpId,
                        UserId = p1.UserId,
                        PlayerName = p1.Name
                    });
                }
                else if (p1 != null && p2 != null)
                {
                    var court = courts[courtIndex % courts.Count];
                    courtIndex++;

                    matchesOut.Add(new RoundRobinMatch
                    {
                        OrganizationId = rrEvent.OrganizationId,
                        RoundRobinEventId = rrEvent.Id,
                        RoundNumber = round,
                        CourtId = court.Id,
                        CourtName = court.Name,
                        EstimatedStartTime = currentStart,
                        EstimatedEndTime = roundEnd,
                        Status = MatchStatus.Scheduled,
                        Team1Player1RsvpId = p1.RsvpId,
                        Team1Player1UserId = p1.UserId,
                        Team1Player1Name = p1.Name,
                        Team2Player1RsvpId = p2.RsvpId,
                        Team2Player1UserId = p2.UserId,
                        Team2Player1Name = p2.Name
                    });
                }
            }

            // Rotate pool keeping index 0 fixed
            var last = pool[poolSize - 1];
            pool.RemoveAt(poolSize - 1);
            pool.Insert(1, last);

            currentStart = roundEnd.Add(breakDuration);
        }
    }

    private void GenerateFixedPartnersSchedule(
        RoundRobinEvent rrEvent,
        List<PlayerInfo> players,
        List<Court> courts,
        int totalRounds,
        TimeSpan activityStartTime,
        TimeSpan matchDuration,
        TimeSpan breakDuration,
        List<RoundRobinMatch> matchesOut,
        List<RoundRobinBye> byesOut)
    {
        // Form doubles teams
        var teams = new List<(PlayerInfo p1, PlayerInfo p2)>();
        for (int i = 0; i < players.Count - 1; i += 2)
        {
            teams.Add((players[i], players[i + 1]));
        }

        // Leftover player gets byes
        if (players.Count % 2 != 0)
        {
            var solo = players.Last();
            for (int r = 1; r <= totalRounds; r++)
            {
                byesOut.Add(new RoundRobinBye
                {
                    OrganizationId = rrEvent.OrganizationId,
                    RoundRobinEventId = rrEvent.Id,
                    RoundNumber = r,
                    ActivityRsvpId = solo.RsvpId,
                    UserId = solo.UserId,
                    PlayerName = solo.Name
                });
            }
        }

        var teamPool = new List<(PlayerInfo p1, PlayerInfo p2)?>();
        foreach (var t in teams) teamPool.Add(t);
        if (teamPool.Count % 2 != 0) teamPool.Add(null); // Dummy team for byes

        var poolSize = teamPool.Count;
        var currentStart = activityStartTime;

        for (int round = 1; round <= totalRounds; round++)
        {
            var roundEnd = currentStart.Add(matchDuration);
            var courtIndex = 0;

            for (int i = 0; i < poolSize / 2; i++)
            {
                var t1 = teamPool[i];
                var t2 = teamPool[poolSize - 1 - i];

                if (t1 == null && t2 != null)
                {
                    byesOut.Add(new RoundRobinBye { OrganizationId = rrEvent.OrganizationId, RoundRobinEventId = rrEvent.Id, RoundNumber = round, ActivityRsvpId = t2.Value.p1.RsvpId, UserId = t2.Value.p1.UserId, PlayerName = t2.Value.p1.Name });
                    byesOut.Add(new RoundRobinBye { OrganizationId = rrEvent.OrganizationId, RoundRobinEventId = rrEvent.Id, RoundNumber = round, ActivityRsvpId = t2.Value.p2.RsvpId, UserId = t2.Value.p2.UserId, PlayerName = t2.Value.p2.Name });
                }
                else if (t2 == null && t1 != null)
                {
                    byesOut.Add(new RoundRobinBye { OrganizationId = rrEvent.OrganizationId, RoundRobinEventId = rrEvent.Id, RoundNumber = round, ActivityRsvpId = t1.Value.p1.RsvpId, UserId = t1.Value.p1.UserId, PlayerName = t1.Value.p1.Name });
                    byesOut.Add(new RoundRobinBye { OrganizationId = rrEvent.OrganizationId, RoundRobinEventId = rrEvent.Id, RoundNumber = round, ActivityRsvpId = t1.Value.p2.RsvpId, UserId = t1.Value.p2.UserId, PlayerName = t1.Value.p2.Name });
                }
                else if (t1 != null && t2 != null)
                {
                    var court = courts[courtIndex % courts.Count];
                    courtIndex++;

                    matchesOut.Add(new RoundRobinMatch
                    {
                        OrganizationId = rrEvent.OrganizationId,
                        RoundRobinEventId = rrEvent.Id,
                        RoundNumber = round,
                        CourtId = court.Id,
                        CourtName = court.Name,
                        EstimatedStartTime = currentStart,
                        EstimatedEndTime = roundEnd,
                        Status = MatchStatus.Scheduled,
                        Team1Player1RsvpId = t1.Value.p1.RsvpId,
                        Team1Player1UserId = t1.Value.p1.UserId,
                        Team1Player1Name = t1.Value.p1.Name,
                        Team1Player2RsvpId = t1.Value.p2.RsvpId,
                        Team1Player2UserId = t1.Value.p2.UserId,
                        Team1Player2Name = t1.Value.p2.Name,
                        Team2Player1RsvpId = t2.Value.p1.RsvpId,
                        Team2Player1UserId = t2.Value.p1.UserId,
                        Team2Player1Name = t2.Value.p1.Name,
                        Team2Player2RsvpId = t2.Value.p2.RsvpId,
                        Team2Player2UserId = t2.Value.p2.UserId,
                        Team2Player2Name = t2.Value.p2.Name
                    });
                }
            }

            // Rotate
            var last = teamPool[poolSize - 1];
            teamPool.RemoveAt(poolSize - 1);
            teamPool.Insert(1, last);

            currentStart = roundEnd.Add(breakDuration);
        }
    }

    private void GenerateRotatingPartnersSchedule(
        RoundRobinEvent rrEvent,
        List<PlayerInfo> players,
        List<Court> courts,
        int totalRounds,
        TimeSpan activityStartTime,
        TimeSpan matchDuration,
        TimeSpan breakDuration,
        List<RoundRobinMatch> matchesOut,
        List<RoundRobinBye> byesOut)
    {
        var n = players.Count;
        var maxCourts = courts.Count;
        var courtsToUse = Math.Min(n / 4, maxCourts);
        if (courtsToUse < 1) courtsToUse = 1;

        var playersPerRound = courtsToUse * 4;
        var currentStart = activityStartTime;

        for (int round = 1; round <= totalRounds; round++)
        {
            var roundEnd = currentStart.Add(matchDuration);

            // Rotate players pool for fair byes across rounds
            var shift = ((round - 1) * 3) % n;
            var orderedForRound = new List<PlayerInfo>();
            for (int i = 0; i < n; i++)
            {
                orderedForRound.Add(players[(i + shift) % n]);
            }

            var playingPlayers = orderedForRound.Take(playersPerRound).ToList();
            var byePlayers = orderedForRound.Skip(playersPerRound).ToList();

            foreach (var bye in byePlayers)
            {
                byesOut.Add(new RoundRobinBye
                {
                    OrganizationId = rrEvent.OrganizationId,
                    RoundRobinEventId = rrEvent.Id,
                    RoundNumber = round,
                    ActivityRsvpId = bye.RsvpId,
                    UserId = bye.UserId,
                    PlayerName = bye.Name
                });
            }

            for (int c = 0; c < courtsToUse; c++)
            {
                var court = courts[c % courts.Count];
                var courtPlayers = playingPlayers.Skip(c * 4).Take(4).ToList();

                if (courtPlayers.Count == 4)
                {
                    // Rotate partner combination:
                    // Round % 3 == 1: (0,1) vs (2,3)
                    // Round % 3 == 2: (0,2) vs (1,3)
                    // Round % 3 == 0: (0,3) vs (1,2)
                    int combo = round % 3;
                    PlayerInfo t1p1, t1p2, t2p1, t2p2;

                    if (combo == 1)
                    {
                        t1p1 = courtPlayers[0];
                        t1p2 = courtPlayers[1];
                        t2p1 = courtPlayers[2];
                        t2p2 = courtPlayers[3];
                    }
                    else if (combo == 2)
                    {
                        t1p1 = courtPlayers[0];
                        t1p2 = courtPlayers[2];
                        t2p1 = courtPlayers[1];
                        t2p2 = courtPlayers[3];
                    }
                    else
                    {
                        t1p1 = courtPlayers[0];
                        t1p2 = courtPlayers[3];
                        t2p1 = courtPlayers[1];
                        t2p2 = courtPlayers[2];
                    }

                    matchesOut.Add(new RoundRobinMatch
                    {
                        OrganizationId = rrEvent.OrganizationId,
                        RoundRobinEventId = rrEvent.Id,
                        RoundNumber = round,
                        CourtId = court.Id,
                        CourtName = court.Name,
                        EstimatedStartTime = currentStart,
                        EstimatedEndTime = roundEnd,
                        Status = MatchStatus.Scheduled,
                        Team1Player1RsvpId = t1p1.RsvpId,
                        Team1Player1UserId = t1p1.UserId,
                        Team1Player1Name = t1p1.Name,
                        Team1Player2RsvpId = t1p2.RsvpId,
                        Team1Player2UserId = t1p2.UserId,
                        Team1Player2Name = t1p2.Name,
                        Team2Player1RsvpId = t2p1.RsvpId,
                        Team2Player1UserId = t2p1.UserId,
                        Team2Player1Name = t2p1.Name,
                        Team2Player2RsvpId = t2p2.RsvpId,
                        Team2Player2UserId = t2p2.UserId,
                        Team2Player2Name = t2p2.Name
                    });
                }
            }

            currentStart = roundEnd.Add(breakDuration);
        }
    }

    public async Task<RoundRobinResult> ClearScheduleAsync(int activityId, string adminUserId)
    {
        var orgId = _tenantContext.OrganizationId;
        var rrEvent = await _context.RoundRobinEvents
            .Include(e => e.Matches)
            .Include(e => e.Byes)
            .FirstOrDefaultAsync(e => e.ActivityId == activityId && e.OrganizationId == orgId);

        if (rrEvent == null)
            return new RoundRobinResult(false, "No round robin schedule exists for this activity.");

        if (rrEvent.IsLocked)
            return new RoundRobinResult(false, "Round robin schedule is locked. Unlock it first before clearing.");

        _context.RoundRobinMatches.RemoveRange(rrEvent.Matches);
        _context.RoundRobinByes.RemoveRange(rrEvent.Byes);
        _context.RoundRobinEvents.Remove(rrEvent);
        await _context.SaveChangesAsync();

        return new RoundRobinResult(true, "Round robin schedule has been cleared.");
    }

    public async Task<RoundRobinResult> ToggleLockAsync(int activityId, bool isLocked, string adminUserId)
    {
        var orgId = _tenantContext.OrganizationId;
        var rrEvent = await _context.RoundRobinEvents
            .FirstOrDefaultAsync(e => e.ActivityId == activityId && e.OrganizationId == orgId);

        if (rrEvent == null)
            return new RoundRobinResult(false, "No round robin schedule exists to lock/unlock.");

        rrEvent.IsLocked = isLocked;
        rrEvent.LockedAt = isLocked ? DateTime.UtcNow : null;
        rrEvent.LockedByUserId = isLocked ? adminUserId : null;
        await _context.SaveChangesAsync();

        return new RoundRobinResult(true, isLocked ? "Round robin schedule locked." : "Round robin schedule unlocked.");
    }

    public async Task<RoundRobinResult> SwapMatchPlayersAsync(int matchId, string slotA, string slotB, string adminUserId)
    {
        var orgId = _tenantContext.OrganizationId;
        var match = await _context.RoundRobinMatches
            .Include(m => m.RoundRobinEvent)
            .FirstOrDefaultAsync(m => m.Id == matchId && m.OrganizationId == orgId);

        if (match == null) return new RoundRobinResult(false, "Match not found.");
        if (match.RoundRobinEvent?.IsLocked == true) return new RoundRobinResult(false, "Schedule is locked.");

        // Slots: "t1p1", "t1p2", "t2p1", "t2p2"
        var valA = GetSlot(match, slotA);
        var valB = GetSlot(match, slotB);

        SetSlot(match, slotA, valB);
        SetSlot(match, slotB, valA);

        await _context.SaveChangesAsync();
        return new RoundRobinResult(true, "Players swapped successfully.");
    }

    public async Task<RoundRobinResult> UpdateMatchCourtAsync(int matchId, int newCourtId, string adminUserId)
    {
        var orgId = _tenantContext.OrganizationId;
        var match = await _context.RoundRobinMatches
            .Include(m => m.RoundRobinEvent)
            .FirstOrDefaultAsync(m => m.Id == matchId && m.OrganizationId == orgId);

        if (match == null) return new RoundRobinResult(false, "Match not found.");
        if (match.RoundRobinEvent?.IsLocked == true) return new RoundRobinResult(false, "Schedule is locked.");

        var court = await _context.Courts.FirstOrDefaultAsync(c => c.Id == newCourtId && c.OrganizationId == orgId);
        if (court == null) return new RoundRobinResult(false, "Court not found.");

        match.CourtId = court.Id;
        match.CourtName = court.Name;
        await _context.SaveChangesAsync();

        return new RoundRobinResult(true, $"Match moved to {court.Name}.");
    }

    public async Task<RoundRobinResult> UpdateRoundTimeAsync(int activityId, int roundNumber, TimeSpan newStartTime, TimeSpan newEndTime, string adminUserId)
    {
        var orgId = _tenantContext.OrganizationId;
        var rrEvent = await _context.RoundRobinEvents
            .Include(e => e.Matches)
            .FirstOrDefaultAsync(e => e.ActivityId == activityId && e.OrganizationId == orgId);

        if (rrEvent == null) return new RoundRobinResult(false, "Round robin event not found.");
        if (rrEvent.IsLocked) return new RoundRobinResult(false, "Schedule is locked.");

        var roundMatches = rrEvent.Matches.Where(m => m.RoundNumber == roundNumber).ToList();
        if (roundMatches.Count == 0) return new RoundRobinResult(false, "No matches found for this round.");

        foreach (var m in roundMatches)
        {
            m.EstimatedStartTime = newStartTime;
            m.EstimatedEndTime = newEndTime;
        }

        await _context.SaveChangesAsync();
        return new RoundRobinResult(true, $"Round {roundNumber} times updated to {AppClock.To12HourRange(newStartTime, newEndTime)}.");
    }

    public async Task<PlayerScheduleDto?> GetPlayerScheduleAsync(int activityId, string userId)
    {
        var orgId = _tenantContext.OrganizationId;
        var activity = await _context.Activities
            .FirstOrDefaultAsync(a => a.Id == activityId && a.OrganizationId == orgId);

        if (activity == null) return null;

        var rrEvent = await _context.RoundRobinEvents
            .Include(e => e.Matches)
            .Include(e => e.Byes)
            .FirstOrDefaultAsync(e => e.ActivityId == activityId && e.OrganizationId == orgId);

        if (rrEvent == null) return null;

        var rsvp = await _context.ActivityRsvps
            .FirstOrDefaultAsync(r => r.ActivityId == activityId && r.UserId == userId && r.OrganizationId == orgId);

        if (rsvp == null) return null;

        var profile = await _context.PlayerProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        var playerName = !string.IsNullOrWhiteSpace(profile?.DisplayName) 
            ? profile.DisplayName 
            : $"{profile?.FirstName} {profile?.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(playerName)) playerName = user?.UserName ?? "Player";

        var playerRounds = new List<PlayerRoundMatchDto>();

        for (int r = 1; r <= rrEvent.NumberOfRounds; r++)
        {
            var bye = rrEvent.Byes.FirstOrDefault(b => b.RoundNumber == r && b.ActivityRsvpId == rsvp.Id);
            if (bye != null)
            {
                playerRounds.Add(new PlayerRoundMatchDto(
                    r,
                    null,
                    null,
                    "BYE",
                    true,
                    null,
                    [],
                    null,
                    null,
                    null,
                    null
                ));
                continue;
            }

            var match = rrEvent.Matches.FirstOrDefault(m =>
                m.RoundNumber == r &&
                (m.Team1Player1RsvpId == rsvp.Id || m.Team1Player2RsvpId == rsvp.Id ||
                 m.Team2Player1RsvpId == rsvp.Id || m.Team2Player2RsvpId == rsvp.Id));

            if (match != null)
            {
                bool isTeam1 = (match.Team1Player1RsvpId == rsvp.Id || match.Team1Player2RsvpId == rsvp.Id);
                string? partner = null;
                var opponents = new List<string>();

                if (isTeam1)
                {
                    partner = match.Team1Player1RsvpId == rsvp.Id ? match.Team1Player2Name : match.Team1Player1Name;
                    if (!string.IsNullOrEmpty(match.Team2Player1Name)) opponents.Add(match.Team2Player1Name);
                    if (!string.IsNullOrEmpty(match.Team2Player2Name)) opponents.Add(match.Team2Player2Name);
                }
                else
                {
                    partner = match.Team2Player1RsvpId == rsvp.Id ? match.Team2Player2Name : match.Team2Player1Name;
                    if (!string.IsNullOrEmpty(match.Team1Player1Name)) opponents.Add(match.Team1Player1Name);
                    if (!string.IsNullOrEmpty(match.Team1Player2Name)) opponents.Add(match.Team1Player2Name);
                }

                int? teamScore = isTeam1 ? match.Team1Score : match.Team2Score;
                int? oppScore = isTeam1 ? match.Team2Score : match.Team1Score;

                playerRounds.Add(new PlayerRoundMatchDto(
                    r,
                    match.EstimatedStartTime,
                    match.EstimatedEndTime,
                    match.CourtName ?? "Court",
                    false,
                    partner,
                    opponents,
                    match.Status,
                    teamScore,
                    oppScore,
                    match.WinningSide
                ));
            }
        }

        return new PlayerScheduleDto(
            activity.Id,
            activity.Name,
            playerName,
            rrEvent.Format,
            playerRounds
        );
    }

    private static (int? rsvpId, string? userId, string name) GetSlot(RoundRobinMatch m, string slot) => slot.ToLowerInvariant() switch
    {
        "t1p1" => (m.Team1Player1RsvpId, m.Team1Player1UserId, m.Team1Player1Name),
        "t1p2" => (m.Team1Player2RsvpId, m.Team1Player2UserId, m.Team1Player2Name ?? string.Empty),
        "t2p1" => (m.Team2Player1RsvpId, m.Team2Player1UserId, m.Team2Player1Name),
        "t2p2" => (m.Team2Player2RsvpId, m.Team2Player2UserId, m.Team2Player2Name ?? string.Empty),
        _ => (null, null, string.Empty)
    };

    private static void SetSlot(RoundRobinMatch m, string slot, (int? rsvpId, string? userId, string name) val)
    {
        switch (slot.ToLowerInvariant())
        {
            case "t1p1":
                m.Team1Player1RsvpId = val.rsvpId;
                m.Team1Player1UserId = val.userId;
                m.Team1Player1Name = val.name;
                break;
            case "t1p2":
                m.Team1Player2RsvpId = val.rsvpId;
                m.Team1Player2UserId = val.userId;
                m.Team1Player2Name = string.IsNullOrEmpty(val.name) ? null : val.name;
                break;
            case "t2p1":
                m.Team2Player1RsvpId = val.rsvpId;
                m.Team2Player1UserId = val.userId;
                m.Team2Player1Name = val.name;
                break;
            case "t2p2":
                m.Team2Player2RsvpId = val.rsvpId;
                m.Team2Player2UserId = val.userId;
                m.Team2Player2Name = string.IsNullOrEmpty(val.name) ? null : val.name;
                break;
        }
    }

    private record PlayerInfo(int RsvpId, string UserId, string Name);
}
