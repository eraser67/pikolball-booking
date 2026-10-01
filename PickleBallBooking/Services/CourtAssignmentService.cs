using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public sealed class CourtAssignmentService : ICourtAssignmentService
{
    private readonly ApplicationDbContext _context;
    private readonly ICourtImageStorage _storage;
    private readonly AppNotificationService _notifications;

    public CourtAssignmentService(
        ApplicationDbContext context,
        ICourtImageStorage storage,
        AppNotificationService notifications)
    {
        _context = context;
        _storage = storage;
        _notifications = notifications;
    }

    public async Task<ActivityCourtAssignmentOverviewDto?> GetOverviewAsync(int activityId)
    {
        var activity = await _context.Activities
            .Include(a => a.ActivityCourts).ThenInclude(ac => ac.Court)
            .FirstOrDefaultAsync(a => a.Id == activityId);

        if (activity is null) return null;

        var rsvps = await _context.ActivityRsvps
            .Where(r => r.ActivityId == activityId && (r.Status == RsvpStatus.Confirmed || r.Status == RsvpStatus.CheckedIn || r.Status == RsvpStatus.NoShow))
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();

        var userIds = rsvps.Select(r => r.UserId).Distinct().ToList();
        var profiles = await _context.PlayerProfiles
            .Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId);

        var users = await _context.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        var assignments = await _context.ActivityCourtAssignments
            .Include(ca => ca.Court)
            .Where(ca => ca.ActivityId == activityId)
            .ToListAsync();

        var assignmentByRsvp = assignments.ToDictionary(a => a.ActivityRsvpId);

        CourtAssignedPlayerDto BuildDto(ActivityRsvp r, ActivityCourtAssignment? ca)
        {
            profiles.TryGetValue(r.UserId, out var p);
            users.TryGetValue(r.UserId, out var u);
            var name = p is not null && !string.IsNullOrWhiteSpace(p.DisplayName)
                ? p.DisplayName
                : p is not null
                    ? $"{p.FirstName} {p.LastName}".Trim()
                    : u?.UserName ?? "Player";

            var skill = p?.SkillLevel ?? PlayerSkillLevel.Beginner;
            var avatarUrl = _storage.GetPublicUrl(p?.AvatarPath);
            var isCheckedIn = r.CheckedInAt.HasValue && r.Status != RsvpStatus.NoShow;

            return new CourtAssignedPlayerDto(
                r.Id,
                r.UserId,
                name,
                avatarUrl,
                skill,
                isCheckedIn,
                ca?.CourtId,
                ca?.Court?.Name,
                ca?.SlotNumber ?? 0,
                ca?.AssignedAt
            );
        }

        var courts = activity.ActivityCourts
            .Where(ac => ac.Court != null)
            .Select(ac => ac.Court!)
            .OrderBy(c => c.Name)
            .Select(c =>
            {
                var courtAssignments = assignments
                    .Where(a => a.CourtId == c.Id)
                    .OrderBy(a => a.SlotNumber)
                    .ThenBy(a => a.AssignedAt)
                    .ToList();

                var players = new List<CourtAssignedPlayerDto>();
                foreach (var a in courtAssignments)
                {
                    var rsvp = rsvps.FirstOrDefault(r => r.Id == a.ActivityRsvpId);
                    if (rsvp is not null)
                    {
                        players.Add(BuildDto(rsvp, a));
                    }
                }

                return new CourtGroupDto(c.Id, c.Name, 4, players);
            })
            .ToList();

        var unassigned = rsvps
            .Where(r => !assignmentByRsvp.ContainsKey(r.Id))
            .Select(r => BuildDto(r, null))
            .ToList();

        return new ActivityCourtAssignmentOverviewDto(
            activity.Id,
            activity.Name,
            activity.Date,
            activity.StartTime,
            activity.EndTime,
            activity.Format,
            activity.Status,
            activity.AreCourtAssignmentsLocked,
            activity.CourtAssignmentsLockedAt,
            activity.CourtAssignmentsLockedByUserId,
            courts,
            unassigned,
            rsvps.Count,
            assignments.Count
        );
    }

    public async Task<CourtAssignmentResult> AssignPlayerAsync(
        int activityId,
        int rsvpId,
        int courtId,
        int? slotNumber = null,
        string? adminUserId = null)
    {
        var activity = await _context.Activities
            .Include(a => a.ActivityCourts)
            .FirstOrDefaultAsync(a => a.Id == activityId);

        if (activity is null)
            return new CourtAssignmentResult(false, "Activity not found.");

        if (activity.AreCourtAssignmentsLocked)
            return new CourtAssignmentResult(false, "Court assignments are locked for this activity. Unlock to make changes.");

        var isCourtAllocated = activity.ActivityCourts.Any(ac => ac.CourtId == courtId);
        if (!isCourtAllocated)
            return new CourtAssignmentResult(false, "Target court is not allocated to this activity.");

        var rsvp = await _context.ActivityRsvps
            .Include(r => r.CourtAssignment)
            .FirstOrDefaultAsync(r => r.Id == rsvpId && r.ActivityId == activityId);

        if (rsvp is null)
            return new CourtAssignmentResult(false, "RSVP not found for this activity.");

        if (rsvp.Status == RsvpStatus.Cancelled || rsvp.Status == RsvpStatus.Waitlisted)
            return new CourtAssignmentResult(false, "Only confirmed players can be assigned to a court.");

        var court = await _context.Courts.FirstOrDefaultAsync(c => c.Id == courtId);

        // Calculate slot number if not specified
        int targetSlot = slotNumber ?? 0;
        if (targetSlot <= 0)
        {
            var existingSlots = await _context.ActivityCourtAssignments
                .Where(ca => ca.ActivityId == activityId && ca.CourtId == courtId && ca.ActivityRsvpId != rsvpId)
                .Select(ca => ca.SlotNumber)
                .ToListAsync();

            targetSlot = existingSlots.Count > 0 ? existingSlots.Max() + 1 : 1;
        }

        var assignment = await _context.ActivityCourtAssignments
            .FirstOrDefaultAsync(ca => ca.ActivityId == activityId && ca.ActivityRsvpId == rsvpId);

        if (assignment is not null)
        {
            assignment.CourtId = courtId;
            assignment.SlotNumber = targetSlot;
            assignment.AssignedAt = DateTime.UtcNow;
            assignment.AssignedByUserId = adminUserId;
        }
        else
        {
            assignment = new ActivityCourtAssignment
            {
                OrganizationId = activity.OrganizationId,
                ActivityId = activityId,
                CourtId = courtId,
                ActivityRsvpId = rsvpId,
                UserId = rsvp.UserId,
                SlotNumber = targetSlot,
                AssignedAt = DateTime.UtcNow,
                AssignedByUserId = adminUserId
            };
            _context.ActivityCourtAssignments.Add(assignment);
        }

        _context.SuppressTenantWriteGuard = true;
        await _context.SaveChangesAsync();

        // In-app notification to player
        try
        {
            await _notifications.CreateAsync(
                rsvp.UserId,
                AppNotificationType.ActivityCourtAssigned,
                $"Court Assigned — {activity.Name}",
                $"You have been assigned to {court?.Name ?? "a court"} for {activity.Name}.",
                actionUrl: "/Activities");
        }
        catch
        {
            // Non-critical notification failure should not block assignment
        }

        return new CourtAssignmentResult(true, $"Assigned to {court?.Name ?? "Court"} successfully.", assignment.Id);
    }

    public async Task<CourtAssignmentResult> MovePlayerAsync(
        int activityId,
        int rsvpId,
        int targetCourtId,
        int? newSlotNumber = null,
        string? adminUserId = null)
    {
        return await AssignPlayerAsync(activityId, rsvpId, targetCourtId, newSlotNumber, adminUserId);
    }

    public async Task<CourtAssignmentResult> UnassignPlayerAsync(int activityId, int rsvpId, string? adminUserId = null)
    {
        var activity = await _context.Activities.FirstOrDefaultAsync(a => a.Id == activityId);
        if (activity is null)
            return new CourtAssignmentResult(false, "Activity not found.");

        if (activity.AreCourtAssignmentsLocked)
            return new CourtAssignmentResult(false, "Court assignments are locked for this activity. Unlock to make changes.");

        var assignment = await _context.ActivityCourtAssignments
            .FirstOrDefaultAsync(ca => ca.ActivityId == activityId && ca.ActivityRsvpId == rsvpId);

        if (assignment is null)
            return new CourtAssignmentResult(true, "Player was not assigned to any court.");

        _context.ActivityCourtAssignments.Remove(assignment);
        _context.SuppressTenantWriteGuard = true;
        await _context.SaveChangesAsync();

        return new CourtAssignmentResult(true, "Player unassigned successfully.");
    }

    public async Task<CourtAssignmentResult> AutoAssignAsync(int activityId, string? adminUserId = null)
    {
        var activity = await _context.Activities
            .Include(a => a.ActivityCourts)
            .FirstOrDefaultAsync(a => a.Id == activityId);

        if (activity is null)
            return new CourtAssignmentResult(false, "Activity not found.");

        if (activity.AreCourtAssignmentsLocked)
            return new CourtAssignmentResult(false, "Court assignments are locked for this activity. Unlock to make changes.");

        var courtIds = activity.ActivityCourts.Select(ac => ac.CourtId).OrderBy(id => id).ToList();
        if (courtIds.Count == 0)
            return new CourtAssignmentResult(false, "No courts are allocated to this activity. Allocate courts before assigning players.");

        var rsvps = await _context.ActivityRsvps
            .Where(r => r.ActivityId == activityId && (r.Status == RsvpStatus.Confirmed || r.Status == RsvpStatus.CheckedIn || r.Status == RsvpStatus.NoShow))
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();

        if (rsvps.Count == 0)
            return new CourtAssignmentResult(false, "No confirmed players to assign.");

        // Clear existing assignments for this activity to redistribute evenly
        var existing = await _context.ActivityCourtAssignments
            .Where(ca => ca.ActivityId == activityId)
            .ToListAsync();
        _context.ActivityCourtAssignments.RemoveRange(existing);

        // Distribute round-robin across courts
        var courtSlots = courtIds.ToDictionary(c => c, _ => 0);
        for (int i = 0; i < rsvps.Count; i++)
        {
            var courtId = courtIds[i % courtIds.Count];
            courtSlots[courtId]++;
            var slotNumber = courtSlots[courtId];

            var assignment = new ActivityCourtAssignment
            {
                OrganizationId = activity.OrganizationId,
                ActivityId = activityId,
                CourtId = courtId,
                ActivityRsvpId = rsvps[i].Id,
                UserId = rsvps[i].UserId,
                SlotNumber = slotNumber,
                AssignedAt = DateTime.UtcNow,
                AssignedByUserId = adminUserId
            };
            _context.ActivityCourtAssignments.Add(assignment);
        }

        _context.SuppressTenantWriteGuard = true;
        await _context.SaveChangesAsync();

        return new CourtAssignmentResult(true, $"Successfully distributed {rsvps.Count} players evenly across {courtIds.Count} courts.");
    }

    public async Task<CourtAssignmentResult> SkillBasedGroupAsync(int activityId, string? adminUserId = null)
    {
        var activity = await _context.Activities
            .Include(a => a.ActivityCourts)
            .FirstOrDefaultAsync(a => a.Id == activityId);

        if (activity is null)
            return new CourtAssignmentResult(false, "Activity not found.");

        if (activity.AreCourtAssignmentsLocked)
            return new CourtAssignmentResult(false, "Court assignments are locked for this activity. Unlock to make changes.");

        var courtIds = activity.ActivityCourts.Select(ac => ac.CourtId).OrderBy(id => id).ToList();
        if (courtIds.Count == 0)
            return new CourtAssignmentResult(false, "No courts are allocated to this activity. Allocate courts before grouping players.");

        var rsvps = await _context.ActivityRsvps
            .Where(r => r.ActivityId == activityId && (r.Status == RsvpStatus.Confirmed || r.Status == RsvpStatus.CheckedIn || r.Status == RsvpStatus.NoShow))
            .ToListAsync();

        if (rsvps.Count == 0)
            return new CourtAssignmentResult(false, "No confirmed players to group.");

        var userIds = rsvps.Select(r => r.UserId).Distinct().ToList();
        var profiles = await _context.PlayerProfiles
            .Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId);

        // Sort players descending by skill level (Advanced = 2, Intermediate = 1, Beginner = 0), then by CreatedAt
        var sortedRsvps = rsvps
            .OrderByDescending(r => profiles.TryGetValue(r.UserId, out var p) ? (int)p.SkillLevel : (int)PlayerSkillLevel.Beginner)
            .ThenBy(r => r.CreatedAt)
            .ToList();

        // Clear existing
        var existing = await _context.ActivityCourtAssignments
            .Where(ca => ca.ActivityId == activityId)
            .ToListAsync();
        _context.ActivityCourtAssignments.RemoveRange(existing);

        // Calculate chunk sizes per court so courts are balanced
        int totalPlayers = sortedRsvps.Count;
        int numCourts = courtIds.Count;
        int baseCapacity = totalPlayers / numCourts;
        int remainder = totalPlayers % numCourts;

        int playerIndex = 0;
        for (int c = 0; c < numCourts; c++)
        {
            int courtId = courtIds[c];
            int courtCapacity = baseCapacity + (c < remainder ? 1 : 0);

            for (int slot = 1; slot <= courtCapacity && playerIndex < totalPlayers; slot++)
            {
                var rsvp = sortedRsvps[playerIndex++];
                var assignment = new ActivityCourtAssignment
                {
                    OrganizationId = activity.OrganizationId,
                    ActivityId = activityId,
                    CourtId = courtId,
                    ActivityRsvpId = rsvp.Id,
                    UserId = rsvp.UserId,
                    SlotNumber = slot,
                    AssignedAt = DateTime.UtcNow,
                    AssignedByUserId = adminUserId
                };
                _context.ActivityCourtAssignments.Add(assignment);
            }
        }

        _context.SuppressTenantWriteGuard = true;
        await _context.SaveChangesAsync();

        return new CourtAssignmentResult(true, $"Successfully grouped {rsvps.Count} players by skill level across {courtIds.Count} courts.");
    }

    public async Task<CourtAssignmentResult> RebalanceAsync(int activityId, string? adminUserId = null)
    {
        var activity = await _context.Activities
            .Include(a => a.ActivityCourts)
            .FirstOrDefaultAsync(a => a.Id == activityId);

        if (activity is null)
            return new CourtAssignmentResult(false, "Activity not found.");

        if (activity.AreCourtAssignmentsLocked)
            return new CourtAssignmentResult(false, "Court assignments are locked for this activity. Unlock to make changes.");

        var courtIds = activity.ActivityCourts.Select(ac => ac.CourtId).OrderBy(id => id).ToList();
        if (courtIds.Count == 0)
            return new CourtAssignmentResult(false, "No courts allocated to this activity.");

        var assignments = await _context.ActivityCourtAssignments
            .Where(ca => ca.ActivityId == activityId)
            .ToListAsync();

        if (assignments.Count == 0)
            return new CourtAssignmentResult(true, "No assignments to rebalance.");

        // Check if rebalance is needed: difference between court with most players and least players > 1
        var courtPlayerMap = courtIds.ToDictionary(id => id, id => assignments.Where(a => a.CourtId == id).OrderBy(a => a.SlotNumber).ToList());

        bool changed = false;
        while (true)
        {
            var mostCourt = courtPlayerMap.OrderByDescending(kv => kv.Value.Count).First();
            var leastCourt = courtPlayerMap.OrderBy(kv => kv.Value.Count).First();

            if (mostCourt.Value.Count - leastCourt.Value.Count <= 1)
            {
                break;
            }

            // Move the last player from the most populated court to the least populated court
            var playerToMove = mostCourt.Value.Last();
            mostCourt.Value.RemoveAt(mostCourt.Value.Count - 1);

            playerToMove.CourtId = leastCourt.Key;
            leastCourt.Value.Add(playerToMove);
            changed = true;
        }

        if (changed)
        {
            // Renumber slots per court
            foreach (var kv in courtPlayerMap)
            {
                for (int slot = 0; slot < kv.Value.Count; slot++)
                {
                    kv.Value[slot].SlotNumber = slot + 1;
                }
            }

            _context.SuppressTenantWriteGuard = true;
            await _context.SaveChangesAsync();
            return new CourtAssignmentResult(true, "Courts successfully rebalanced.");
        }

        return new CourtAssignmentResult(true, "Courts are already balanced.");
    }

    public async Task<CourtAssignmentResult> ClearAssignmentsAsync(int activityId, string? adminUserId = null)
    {
        var activity = await _context.Activities.FirstOrDefaultAsync(a => a.Id == activityId);
        if (activity is null)
            return new CourtAssignmentResult(false, "Activity not found.");

        if (activity.AreCourtAssignmentsLocked)
            return new CourtAssignmentResult(false, "Court assignments are locked for this activity. Unlock to make changes.");

        var assignments = await _context.ActivityCourtAssignments
            .Where(ca => ca.ActivityId == activityId)
            .ToListAsync();

        if (assignments.Count > 0)
        {
            _context.ActivityCourtAssignments.RemoveRange(assignments);
            _context.SuppressTenantWriteGuard = true;
            await _context.SaveChangesAsync();
        }

        return new CourtAssignmentResult(true, "All court assignments cleared.");
    }

    public async Task<CourtAssignmentResult> SetLockAsync(int activityId, bool locked, string? adminUserId = null)
    {
        var activity = await _context.Activities.FirstOrDefaultAsync(a => a.Id == activityId);
        if (activity is null)
            return new CourtAssignmentResult(false, "Activity not found.");

        activity.AreCourtAssignmentsLocked = locked;
        activity.CourtAssignmentsLockedAt = locked ? DateTime.UtcNow : null;
        activity.CourtAssignmentsLockedByUserId = locked ? adminUserId : null;

        _context.SuppressTenantWriteGuard = true;
        await _context.SaveChangesAsync();

        return new CourtAssignmentResult(
            true,
            locked ? "Court assignments have been locked." : "Court assignments have been unlocked."
        );
    }

    public async Task<CourtAssignedPlayerDto?> GetPlayerCourtAssignmentAsync(int activityId, string userId)
    {
        var assignment = await _context.ActivityCourtAssignments
            .Include(ca => ca.Court)
            .Include(ca => ca.ActivityRsvp)
            .FirstOrDefaultAsync(ca => ca.ActivityId == activityId && ca.UserId == userId);

        if (assignment is null || assignment.ActivityRsvp is null) return null;

        var profile = await _context.PlayerProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        var name = profile is not null && !string.IsNullOrWhiteSpace(profile.DisplayName)
            ? profile.DisplayName
            : profile is not null
                ? $"{profile.FirstName} {profile.LastName}".Trim()
                : "Player";

        return new CourtAssignedPlayerDto(
            assignment.ActivityRsvpId,
            userId,
            name,
            _storage.GetPublicUrl(profile?.AvatarPath),
            profile?.SkillLevel ?? PlayerSkillLevel.Beginner,
            assignment.ActivityRsvp.CheckedInAt.HasValue && assignment.ActivityRsvp.Status != RsvpStatus.NoShow,
            assignment.CourtId,
            assignment.Court?.Name,
            assignment.SlotNumber,
            assignment.AssignedAt
        );
    }
}
