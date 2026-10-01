using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

/// <summary>
/// Phase 34: Activity RSVP & Waitlist management service.
///
/// Handles player RSVPs, waitlist positioning, automatic promotion upon cancellation,
/// admin waitlist reordering, roster generation, and cross-tenant dashboard queries.
/// All writes and reads for tenant-owned entities respect the EF global query filter.
/// </summary>
public sealed class ActivityRsvpService
{
    private readonly ApplicationDbContext _context;
    private readonly BookingEmailService _email;
    private readonly BookingTelegramService _telegram;
    private readonly AppNotificationService _notifications;
    private readonly ICourtImageStorage _storage;
    private readonly ILogger<ActivityRsvpService> _logger;

    public ActivityRsvpService(
        ApplicationDbContext context,
        BookingEmailService email,
        BookingTelegramService telegram,
        AppNotificationService notifications,
        ICourtImageStorage storage,
        ILogger<ActivityRsvpService> logger)
    {
        _context       = context;
        _email         = email;
        _telegram      = telegram;
        _notifications = notifications;
        _storage       = storage;
        _logger        = logger;
    }

    // ── Player Operations ──────────────────────────────────────────────────

    /// <summary>
    /// Joins an activity as either Confirmed or Waitlisted (if capacity is reached).
    /// </summary>
    public async Task<RsvpResult> JoinActivityAsync(int activityId, string userId, string? notes = null)
    {
        var activity = await _context.Activities
            .Include(a => a.ActivityCourts)
            .FirstOrDefaultAsync(a => a.Id == activityId);

        if (activity is null)
        {
            return new RsvpResult(false, "Activity not found.", null, false);
        }

        // Error handling: The admin for the current organization cannot join their own activities
        if (await IsUserAdminForOrganizationAsync(activity.OrganizationId, userId))
        {
            return new RsvpResult(false, "The admin for the current organization cannot join their own activities.", null, false);
        }

        if (!activity.IsRegistrationOpen())
        {
            if (activity.IsRegistrationPending())
            {
                return new RsvpResult(false, "Registration has not opened yet.", null, false);
            }
            if (activity.IsRegistrationClosed())
            {
                return new RsvpResult(false, "Registration for this activity has closed.", null, false);
            }
            return new RsvpResult(false, "This activity is not currently open for registration.", null, false);
        }

        // Check for existing RSVP
        var existing = await _context.ActivityRsvps
            .FirstOrDefaultAsync(r => r.ActivityId == activityId && r.UserId == userId);

        if (existing is not null)
        {
            if (existing.Status == RsvpStatus.Confirmed)
            {
                return new RsvpResult(false, "You have already secured a confirmed spot for this activity.", existing, false);
            }
            if (existing.Status == RsvpStatus.Waitlisted)
            {
                return new RsvpResult(false, $"You are already on the waitlist at position #{existing.WaitlistPosition}.", existing, true);
            }
        }

        // Determine capacity and status
        var confirmedCount = await _context.ActivityRsvps
            .CountAsync(r => r.ActivityId == activityId && r.Status == RsvpStatus.Confirmed);

        bool isWaitlisted = activity.MaxCapacity > 0 && confirmedCount >= activity.MaxCapacity;
        ActivityRsvp rsvp;

        if (isWaitlisted)
        {
            var maxPosition = await _context.ActivityRsvps
                .Where(r => r.ActivityId == activityId && r.Status == RsvpStatus.Waitlisted)
                .MaxAsync(r => (int?)r.WaitlistPosition) ?? 0;

            var waitlistPos = maxPosition + 1;

            if (existing is not null)
            {
                rsvp = existing;
                rsvp.Status = RsvpStatus.Waitlisted;
                rsvp.WaitlistPosition = waitlistPos;
                rsvp.Notes = notes;
                rsvp.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                rsvp = new ActivityRsvp
                {
                    ActivityId = activityId,
                    UserId = userId,
                    Status = RsvpStatus.Waitlisted,
                    WaitlistPosition = waitlistPos,
                    Notes = notes,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };
                _context.ActivityRsvps.Add(rsvp);
            }

            if (activity.Status == ActivityStatus.RegistrationOpen)
            {
                activity.Status = ActivityStatus.Full;
            }

            await _context.SaveChangesAsync();

            // Email
            await SendWaitlistEmailAsync(activity, userId, waitlistPos);
            // In-app notification
            await _notifications.CreateAsync(
                userId,
                AppNotificationType.ActivityWaitlisted,
                $"Waitlisted — {activity.Name}",
                $"The activity is full. You are at waitlist position #{waitlistPos}. You will be notified if a spot opens up.",
                actionUrl: $"/Customer/Dashboard");

            // Admin alert
            await SendAdminNewRegistrationAlertAsync(activity, userId);

            return new RsvpResult(true, $"Activity is full. You have been added to the waitlist at position #{waitlistPos}.", rsvp, true);
        }
        else
        {
            if (existing is not null)
            {
                rsvp = existing;
                rsvp.Status = RsvpStatus.Confirmed;
                rsvp.WaitlistPosition = null;
                rsvp.Notes = notes;
                rsvp.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                rsvp = new ActivityRsvp
                {
                    ActivityId = activityId,
                    UserId = userId,
                    Status = RsvpStatus.Confirmed,
                    WaitlistPosition = null,
                    Notes = notes,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };
                _context.ActivityRsvps.Add(rsvp);
            }

            if (activity.MaxCapacity > 0 && (confirmedCount + 1) >= activity.MaxCapacity)
            {
                activity.Status = ActivityStatus.Full;
            }

            await _context.SaveChangesAsync();

            // Email
            await SendConfirmationEmailAsync(activity, userId);
            // In-app notification
            await _notifications.CreateAsync(
                userId,
                AppNotificationType.ActivityRsvpConfirmed,
                $"Spot Confirmed — {activity.Name}",
                $"Your registration for {activity.Name} on {activity.Date:MMMM d, yyyy} is confirmed.",
                actionUrl: $"/Customer/Dashboard");
            // Admin alert
            await SendAdminNewRegistrationAlertAsync(activity, userId);

            return new RsvpResult(true, "Your spot has been confirmed!", rsvp, false);
        }
    }

    /// <summary>
    /// Cancels an active RSVP (Confirmed or Waitlisted).
    /// If a Confirmed RSVP is cancelled, the first eligible waitlisted player is automatically promoted!
    /// </summary>
    public async Task<CancelRsvpResult> CancelRsvpAsync(int activityId, string userId, bool isAdmin = false)
    {
        var rsvp = await _context.ActivityRsvps
            .Include(r => r.Activity)
            .FirstOrDefaultAsync(r => r.ActivityId == activityId && r.UserId == userId);

        if (rsvp is null || rsvp.Status is RsvpStatus.Cancelled)
        {
            return new CancelRsvpResult(false, "No active RSVP found to cancel.", null);
        }

        var oldStatus = rsvp.Status;
        rsvp.Status = RsvpStatus.Cancelled;
        rsvp.WaitlistPosition = null;
        rsvp.UpdatedAt = DateTime.UtcNow;

        ActivityRsvp? promotedRsvp = null;
        var activity = rsvp.Activity ?? await _context.Activities.FirstOrDefaultAsync(a => a.Id == activityId);

        if (oldStatus == RsvpStatus.Confirmed && activity is not null)
        {
            // Automatic promotion: find first waitlisted player
            promotedRsvp = await _context.ActivityRsvps
                .Where(r => r.ActivityId == activityId && r.Status == RsvpStatus.Waitlisted)
                .OrderBy(r => r.WaitlistPosition)
                .ThenBy(r => r.CreatedAt)
                .FirstOrDefaultAsync();

            if (promotedRsvp is not null)
            {
                promotedRsvp.Status = RsvpStatus.Confirmed;
                promotedRsvp.WaitlistPosition = null;
                promotedRsvp.UpdatedAt = DateTime.UtcNow;
            }

            // Re-index remaining waitlisted players (1-based)
            await ReindexWaitlistAsync(activityId, promotedRsvp?.Id);

            // Re-evaluate activity status
            var confirmedCount = await _context.ActivityRsvps
                .CountAsync(r => r.ActivityId == activityId && r.Status == RsvpStatus.Confirmed && r.Id != rsvp.Id);

            if (promotedRsvp is not null)
            {
                confirmedCount++;
            }

            var waitlistCount = await _context.ActivityRsvps
                .CountAsync(r => r.ActivityId == activityId && r.Status == RsvpStatus.Waitlisted && r.Id != (promotedRsvp != null ? promotedRsvp.Id : 0));

            if (activity.Status == ActivityStatus.Full && waitlistCount == 0 && (activity.MaxCapacity == 0 || confirmedCount < activity.MaxCapacity))
            {
                activity.Status = ActivityStatus.RegistrationOpen;
            }
        }
        else if (oldStatus == RsvpStatus.Waitlisted && activity is not null)
        {
            // Re-index remaining waitlist
            await ReindexWaitlistAsync(activityId, rsvp.Id);

            var remainingWaitlistCount = await _context.ActivityRsvps
                .CountAsync(r => r.ActivityId == activityId && r.Status == RsvpStatus.Waitlisted && r.Id != rsvp.Id);

            var confirmedCount = await _context.ActivityRsvps
                .CountAsync(r => r.ActivityId == activityId && r.Status == RsvpStatus.Confirmed);

            if (activity.Status == ActivityStatus.Full && remainingWaitlistCount == 0 && (activity.MaxCapacity == 0 || confirmedCount < activity.MaxCapacity))
            {
                activity.Status = ActivityStatus.RegistrationOpen;
            }
        }

        await _context.SaveChangesAsync();

        if (activity is not null)
        {
            // Email cancellation
            await SendCancellationEmailAsync(activity, userId);
            // In-app: cancellation
            await _notifications.CreateAsync(
                userId,
                AppNotificationType.ActivityRsvpCancelled,
                $"RSVP Cancelled — {activity.Name}",
                $"Your registration for {activity.Name} on {activity.Date:MMMM d, yyyy} has been cancelled.",
                actionUrl: $"/Customer/Dashboard");

            if (promotedRsvp is not null)
            {
                await SendPromotedEmailAsync(activity, promotedRsvp.UserId);
                // In-app: promotion
                await _notifications.CreateAsync(
                    promotedRsvp.UserId,
                    AppNotificationType.ActivityWaitlistPromoted,
                    $"You've Been Promoted! — {activity.Name}",
                    $"A spot opened up for {activity.Name} on {activity.Date:MMMM d, yyyy}. Your spot is now confirmed!",
                    actionUrl: $"/Customer/Dashboard");
            }

            // Alert admin / staff across Email, Telegram, and In-App when a player cancels their RSVP
            if (!isAdmin)
            {
                await SendAdminRsvpCancelledAlertAsync(activity, userId, promotedRsvp?.UserId);
            }
        }

        return new CancelRsvpResult(true, "RSVP has been successfully cancelled.", promotedRsvp);
    }

    // ── Admin Operations ───────────────────────────────────────────────────

    /// <summary>
    /// Admin manually promotes a waitlisted player to Confirmed regardless of waitlist order.
    /// </summary>
    public async Task<(bool Success, string Message)> PromoteWaitlistedAsync(int rsvpId)
    {
        var rsvp = await _context.ActivityRsvps
            .Include(r => r.Activity)
            .FirstOrDefaultAsync(r => r.Id == rsvpId);

        if (rsvp is null)
        {
            return (false, "RSVP entry not found.");
        }

        if (rsvp.Status != RsvpStatus.Waitlisted)
        {
            return (false, "Player is not currently on the waitlist.");
        }

        var activity = rsvp.Activity;
        if (activity is null)
        {
            return (false, "Activity not found.");
        }

        rsvp.Status = RsvpStatus.Confirmed;
        rsvp.WaitlistPosition = null;
        rsvp.UpdatedAt = DateTime.UtcNow;

        // Re-index remaining waitlist
        await ReindexWaitlistAsync(activity.Id, rsvp.Id);

        var confirmedCount = await _context.ActivityRsvps
            .CountAsync(r => r.ActivityId == activity.Id && r.Status == RsvpStatus.Confirmed && r.Id != rsvp.Id) + 1;

        if (activity.MaxCapacity > 0 && confirmedCount >= activity.MaxCapacity)
        {
            activity.Status = ActivityStatus.Full;
        }

        await _context.SaveChangesAsync();

        await SendPromotedEmailAsync(activity, rsvp.UserId);
        // In-app: promotion
        await _notifications.CreateAsync(
            rsvp.UserId,
            AppNotificationType.ActivityWaitlistPromoted,
            $"You've Been Promoted! — {activity.Name}",
            $"A spot opened up for {activity.Name} on {activity.Date:MMMM d, yyyy}. Your spot is now confirmed!",
            actionUrl: $"/Customer/Dashboard");

        return (true, "Player has been promoted to Confirmed.");
    }

    /// <summary>
    /// Swaps the position of a waitlisted player with an adjacent waitlisted player (Move Up or Down).
    /// </summary>
    public async Task<(bool Success, string Message)> MoveWaitlistPositionAsync(int rsvpId, bool moveUp)
    {
        var target = await _context.ActivityRsvps.FirstOrDefaultAsync(r => r.Id == rsvpId);
        if (target is null || target.Status != RsvpStatus.Waitlisted || !target.WaitlistPosition.HasValue)
        {
            return (false, "Player is not on the waitlist.");
        }

        var currentPos = target.WaitlistPosition.Value;
        var desiredPos = moveUp ? currentPos - 1 : currentPos + 1;

        if (desiredPos < 1)
        {
            return (false, "Player is already at the front of the waitlist.");
        }

        var swapWith = await _context.ActivityRsvps
            .FirstOrDefaultAsync(r => r.ActivityId == target.ActivityId
                                   && r.Status == RsvpStatus.Waitlisted
                                   && r.WaitlistPosition == desiredPos);

        if (swapWith is null)
        {
            return (false, moveUp ? "Already at top." : "Already at bottom.");
        }

        // Swap positions
        target.WaitlistPosition = desiredPos;
        swapWith.WaitlistPosition = currentPos;
        target.UpdatedAt = DateTime.UtcNow;
        swapWith.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return (true, $"Waitlist position updated to #{desiredPos}.");
    }

    // ── Queries ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the active RSVP for a given user in an activity, if any.
    /// </summary>
    public async Task<ActivityRsvp?> GetUserRsvpAsync(int activityId, string userId)
    {
        return await _context.ActivityRsvps
            .FirstOrDefaultAsync(r => r.ActivityId == activityId
                                   && r.UserId == userId
                                   && (r.Status == RsvpStatus.Confirmed || r.Status == RsvpStatus.Waitlisted));
    }

    /// <summary>
    /// Returns a map of ActivityId -> User's active RSVP for a list of activities.
    /// </summary>
    public async Task<Dictionary<int, ActivityRsvp>> GetUserRsvpsForActivitiesAsync(IEnumerable<int> activityIds, string userId)
    {
        var idList = activityIds.ToList();
        if (!idList.Any() || string.IsNullOrEmpty(userId))
        {
            return new Dictionary<int, ActivityRsvp>();
        }

        return await _context.ActivityRsvps
            .Where(r => idList.Contains(r.ActivityId)
                     && r.UserId == userId
                     && (r.Status == RsvpStatus.Confirmed || r.Status == RsvpStatus.Waitlisted))
            .ToDictionaryAsync(r => r.ActivityId);
    }

    /// <summary>
    /// Returns count of confirmed players and waitlisted players for a list of activities.
    /// </summary>
    public async Task<Dictionary<int, (int Confirmed, int Waitlist)>> GetRsvpCountsForActivitiesAsync(IEnumerable<int> activityIds)
    {
        var idList = activityIds.ToList();
        if (!idList.Any()) return new Dictionary<int, (int, int)>();

        var grouped = await _context.ActivityRsvps
            .Where(r => idList.Contains(r.ActivityId) && (r.Status == RsvpStatus.Confirmed || r.Status == RsvpStatus.Waitlisted))
            .GroupBy(r => new { r.ActivityId, r.Status })
            .Select(g => new { g.Key.ActivityId, g.Key.Status, Count = g.Count() })
            .ToListAsync();

        var result = idList.ToDictionary(id => id, _ => (0, 0));
        foreach (var item in grouped)
        {
            var current = result[item.ActivityId];
            if (item.Status == RsvpStatus.Confirmed)
            {
                result[item.ActivityId] = (item.Count, current.Item2);
            }
            else if (item.Status == RsvpStatus.Waitlisted)
            {
                result[item.ActivityId] = (current.Item1, item.Count);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the full roster (Confirmed, Waitlisted, Cancelled) for an activity.
    /// </summary>
    public async Task<ActivityRosterResult?> GetRosterAsync(int activityId)
    {
        var activity = await _context.Activities
            .Include(a => a.ActivityCourts).ThenInclude(ac => ac.Court)
            .FirstOrDefaultAsync(a => a.Id == activityId);

        if (activity is null) return null;

        var rsvps = await _context.ActivityRsvps
            .Include(r => r.User)
            .Where(r => r.ActivityId == activityId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();

        var userIds = rsvps.Select(r => r.UserId).Distinct().ToList();
        var profiles = await _context.PlayerProfiles
            .Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId);

        var playerItems = rsvps.Select(r =>
        {
            profiles.TryGetValue(r.UserId, out var p);
            var name = p is not null && !string.IsNullOrWhiteSpace(p.DisplayName)
                ? p.DisplayName
                : p is not null
                    ? $"{p.FirstName} {p.LastName}".Trim()
                    : r.User?.UserName ?? "Player";

            var email = r.User?.Email ?? string.Empty;
            var mobile = p?.Mobile ?? r.User?.PhoneNumber;
            var avatarUrl = _storage.GetPublicUrl(p?.AvatarPath);

            return new RsvpPlayerItem(
                r.Id,
                r.UserId,
                name,
                email,
                mobile,
                p?.SkillLevel,
                avatarUrl,
                r.Status,
                r.WaitlistPosition,
                r.CreatedAt,
                r.CheckedInAt,
                r.CheckInMethod);
        }).ToList();

        var confirmed = playerItems.Where(p => p.Status is RsvpStatus.Confirmed or RsvpStatus.CheckedIn or RsvpStatus.NoShow).ToList();
        var waitlisted = playerItems.Where(p => p.Status == RsvpStatus.Waitlisted)
            .OrderBy(p => p.WaitlistPosition ?? int.MaxValue)
            .ThenBy(p => p.CreatedAt)
            .ToList();
        var cancelled = playerItems.Where(p => p.Status == RsvpStatus.Cancelled).ToList();

        return new ActivityRosterResult(
            activity,
            confirmed,
            waitlisted,
            cancelled,
            activity.MaxCapacity,
            confirmed.Count,
            waitlisted.Count,
            confirmed.Count(p => p.Status == RsvpStatus.CheckedIn),
            confirmed.Count(p => p.Status == RsvpStatus.NoShow));
    }

    /// <summary>
    /// Cross-tenant customer activities lookup for the customer dashboard (/Customer/Dashboard).
    /// </summary>
    public async Task<List<UserActivityItem>> GetUserActivitiesAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return [];

        var rsvps = await _context.ActivityRsvps
            .IgnoreQueryFilters()
            .Include(r => r.Activity)
                .ThenInclude(a => a!.ActivityCourts)
                    .ThenInclude(ac => ac.Court)
            .Where(r => r.UserId == userId && (r.Status == RsvpStatus.Confirmed || r.Status == RsvpStatus.Waitlisted || r.Status == RsvpStatus.CheckedIn || r.Status == RsvpStatus.NoShow))
            .OrderBy(r => r.Activity != null ? r.Activity.Date : DateOnly.MinValue)
            .ThenBy(r => r.Activity != null ? r.Activity.StartTime : TimeSpan.Zero)
            .ToListAsync();

        var orgIds = rsvps.Select(r => r.OrganizationId).Distinct().ToList();
        var orgs = await _context.Organizations
            .IgnoreQueryFilters()
            .Where(o => orgIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id);

        return rsvps.Select(r =>
        {
            var a = r.Activity!;
            orgs.TryGetValue(r.OrganizationId, out var org);
            var courts = a.ActivityCourts.Any()
                ? string.Join(", ", a.ActivityCourts.Select(ac => ac.Court?.Name ?? "?"))
                : "TBD";

            return new UserActivityItem(
                r.Id,
                a.Id,
                a.Name,
                org?.Name ?? "Venue",
                org?.Slug ?? "pikolball",
                a.Date,
                a.StartTime,
                a.EndTime,
                a.Format,
                a.SkillLevel,
                a.PricePerPlayer,
                courts,
                r.Status,
                r.WaitlistPosition,
                r.CreatedAt,
                r.CheckedInAt,
                r.CheckInMethod);
        }).ToList();
    }

    // ── Internal Helpers ───────────────────────────────────────────────────

    private async Task ReindexWaitlistAsync(int activityId, int? excludeRsvpId)
    {
        var waitlist = await _context.ActivityRsvps
            .Where(r => r.ActivityId == activityId
                     && r.Status == RsvpStatus.Waitlisted
                     && (!excludeRsvpId.HasValue || r.Id != excludeRsvpId.Value))
            .OrderBy(r => r.WaitlistPosition)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync();

        for (int i = 0; i < waitlist.Count; i++)
        {
            waitlist[i].WaitlistPosition = i + 1;
        }
    }

    private async Task<(Organization? Org, string Email, string Name)> GetUserInfoAsync(Activity activity, string userId)
    {
        var org = await _context.Organizations.FirstOrDefaultAsync(o => o.Id == activity.OrganizationId);
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        var profile = await _context.PlayerProfiles.FirstOrDefaultAsync(p => p.UserId == userId);

        var email = user?.Email ?? string.Empty;
        var name = profile is not null && !string.IsNullOrWhiteSpace(profile.DisplayName)
            ? profile.DisplayName
            : profile is not null
                ? $"{profile.FirstName} {profile.LastName}".Trim()
                : user?.UserName ?? "Player";

        return (org, email, name);
    }

    private async Task SendConfirmationEmailAsync(Activity activity, string userId)
    {
        try
        {
            var (org, email, name) = await GetUserInfoAsync(activity, userId);
            if (org is not null && !string.IsNullOrWhiteSpace(email))
            {
                _email.SendActivityRsvpConfirmedAsync(activity, org, email, name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send RSVP confirmation email for activity {ActivityId}", activity.Id);
        }
    }

    private async Task SendWaitlistEmailAsync(Activity activity, string userId, int position)
    {
        try
        {
            var (org, email, name) = await GetUserInfoAsync(activity, userId);
            if (org is not null && !string.IsNullOrWhiteSpace(email))
            {
                _email.SendActivityWaitlistJoinedAsync(activity, org, email, name, position);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send waitlist email for activity {ActivityId}", activity.Id);
        }
    }

    private async Task SendPromotedEmailAsync(Activity activity, string userId)
    {
        try
        {
            var (org, email, name) = await GetUserInfoAsync(activity, userId);
            if (org is not null && !string.IsNullOrWhiteSpace(email))
            {
                _email.SendActivityWaitlistPromotedAsync(activity, org, email, name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send waitlist promotion email for activity {ActivityId}", activity.Id);
        }
    }

    private async Task SendCancellationEmailAsync(Activity activity, string userId)
    {
        try
        {
            var (org, email, name) = await GetUserInfoAsync(activity, userId);
            if (org is not null && !string.IsNullOrWhiteSpace(email))
            {
                _email.SendActivityRsvpCancelledAsync(activity, org, email, name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send RSVP cancellation email for activity {ActivityId}", activity.Id);
        }
    }

    private async Task SendAdminNewRegistrationAlertAsync(Activity activity, string userId)
    {
        try
        {
            var org = await _context.Organizations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(o => o.Id == activity.OrganizationId);
            var profile = await _context.PlayerProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            var user    = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            var playerName = profile is not null && !string.IsNullOrWhiteSpace(profile.DisplayName)
                ? profile.DisplayName
                : profile is not null
                    ? $"{profile.FirstName} {profile.LastName}".Trim()
                    : user?.UserName ?? "Player";

            if (org is not null)
            {
                // Find organization admin / owner user IDs
                var adminMembers = await _context.OrganizationMembers
                    .IgnoreQueryFilters()
                    .Where(m => m.OrganizationId == activity.OrganizationId
                             && (m.Role == OrganizationRole.OrganizationOwner || m.Role == OrganizationRole.OrganizationAdmin))
                    .ToListAsync();

                var adminUserIds = adminMembers.Select(m => m.UserId).Distinct().ToList();

                // If no explicit tenant admin found, fall back to platform admin(s)
                if (!adminUserIds.Any())
                {
                    var platformAdminRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == PlatformRoles.PlatformAdmin);
                    if (platformAdminRole is not null)
                    {
                        var platformAdminUserIds = await _context.UserRoles
                            .Where(ur => ur.RoleId == platformAdminRole.Id)
                            .Select(ur => ur.UserId)
                            .ToListAsync();
                        adminUserIds.AddRange(platformAdminUserIds);
                    }
                }

                // Find owner/admin email as fallback if org.NotificationEmail is not set
                string? fallbackEmail = null;
                if (string.IsNullOrWhiteSpace(org.NotificationEmail) && adminUserIds.Any())
                {
                    var ownerUser = await _context.Users
                        .Where(u => adminUserIds.Contains(u.Id))
                        .OrderBy(u => u.Id)
                        .FirstOrDefaultAsync();
                    fallbackEmail = ownerUser?.Email;
                }

                // 1. Email notification to org or owner fallback
                _email.SendActivityNewRegistrationToOrgAsync(
                    activity,
                    org,
                    playerName,
                    user?.Email ?? string.Empty,
                    fallbackRecipientEmail: fallbackEmail);

                // 2. Telegram notification
                _telegram.SendActivityNewRegistrationAlertAsync(activity, org, playerName);

                // 3. In-app notifications for organization admins/owners
                if (adminUserIds.Any())
                {
                    await _notifications.CreateBulkAsync(
                        adminUserIds,
                        AppNotificationType.ActivityNewRegistration,
                        $"New Registration — {activity.Name}",
                        $"{playerName} registered for {activity.Name} on {activity.Date:MMMM d, yyyy}.",
                        actionUrl: $"/Admin/Activities/{activity.Id}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send admin new-registration alert for activity {ActivityId}", activity.Id);
        }
    }

    private async Task SendAdminRsvpCancelledAlertAsync(Activity activity, string cancelledUserId, string? promotedUserId)
    {
        try
        {
            var org = await _context.Organizations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(o => o.Id == activity.OrganizationId);
            var cancelledProfile = await _context.PlayerProfiles.FirstOrDefaultAsync(p => p.UserId == cancelledUserId);
            var cancelledUser    = await _context.Users.FirstOrDefaultAsync(u => u.Id == cancelledUserId);
            var cancelledPlayerName = cancelledProfile is not null && !string.IsNullOrWhiteSpace(cancelledProfile.DisplayName)
                ? cancelledProfile.DisplayName
                : cancelledProfile is not null
                    ? $"{cancelledProfile.FirstName} {cancelledProfile.LastName}".Trim()
                    : cancelledUser?.UserName ?? "Player";

            string? promotedPlayerName = null;
            if (!string.IsNullOrWhiteSpace(promotedUserId))
            {
                var promotedProfile = await _context.PlayerProfiles.FirstOrDefaultAsync(p => p.UserId == promotedUserId);
                var promotedUser    = await _context.Users.FirstOrDefaultAsync(u => u.Id == promotedUserId);
                promotedPlayerName = promotedProfile is not null && !string.IsNullOrWhiteSpace(promotedProfile.DisplayName)
                    ? promotedProfile.DisplayName
                    : promotedProfile is not null
                        ? $"{promotedProfile.FirstName} {promotedProfile.LastName}".Trim()
                        : promotedUser?.UserName ?? "Promoted Player";
            }

            if (org is not null)
            {
                // Find organization admin / owner user IDs
                var adminMembers = await _context.OrganizationMembers
                    .IgnoreQueryFilters()
                    .Where(m => m.OrganizationId == activity.OrganizationId
                             && (m.Role == OrganizationRole.OrganizationOwner || m.Role == OrganizationRole.OrganizationAdmin))
                    .ToListAsync();

                var adminUserIds = adminMembers.Select(m => m.UserId).Distinct().ToList();

                // If no explicit tenant admin found, fall back to platform admin(s)
                if (!adminUserIds.Any())
                {
                    var platformAdminRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == PlatformRoles.PlatformAdmin);
                    if (platformAdminRole is not null)
                    {
                        var platformAdminUserIds = await _context.UserRoles
                            .Where(ur => ur.RoleId == platformAdminRole.Id)
                            .Select(ur => ur.UserId)
                            .ToListAsync();
                        adminUserIds.AddRange(platformAdminUserIds);
                    }
                }

                // Find owner/admin email as fallback if org.NotificationEmail is not set
                string? fallbackEmail = null;
                if (string.IsNullOrWhiteSpace(org.NotificationEmail) && adminUserIds.Any())
                {
                    var ownerUser = await _context.Users
                        .Where(u => adminUserIds.Contains(u.Id))
                        .OrderBy(u => u.Id)
                        .FirstOrDefaultAsync();
                    fallbackEmail = ownerUser?.Email;
                }

                // 1. Email notification to org or owner fallback
                _email.SendActivityRsvpCancelledToOrgAsync(
                    activity,
                    org,
                    cancelledPlayerName,
                    cancelledUser?.Email ?? string.Empty,
                    promotedPlayerName: promotedPlayerName,
                    fallbackRecipientEmail: fallbackEmail);

                // 2. Telegram notification
                _telegram.SendActivityRsvpCancelledAlertAsync(activity, org, cancelledPlayerName, promotedPlayerName);

                // 3. In-app notifications for organization admins/owners
                if (adminUserIds.Any())
                {
                    var promoMsg = !string.IsNullOrWhiteSpace(promotedPlayerName)
                        ? $" Waitlisted player {promotedPlayerName} was automatically promoted."
                        : string.Empty;

                    await _notifications.CreateBulkAsync(
                        adminUserIds,
                        AppNotificationType.ActivityRsvpCancelledAdmin,
                        $"RSVP Cancelled — {activity.Name}",
                        $"{cancelledPlayerName} cancelled their registration for {activity.Name} on {activity.Date:MMMM d, yyyy}.{promoMsg}",
                        actionUrl: $"/Admin/Activities/{activity.Id}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send admin RSVP cancellation alert for activity {ActivityId}", activity.Id);
        }
    }

    /// <summary>
    /// Phase 36: Bulk-notifies all confirmed and waitlisted players that an activity has been cancelled.
    /// Fires email + in-app notifications for each player and sends a Telegram admin alert.
    /// Safe to call fire-and-forget: individual failures are caught and logged.
    /// </summary>
    public async Task NotifyActivityCancelledAsync(Activity activity, BookingTelegramService? telegram = null)
    {
        try
        {
            var org = await _context.Organizations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(o => o.Id == activity.OrganizationId);

            // Fetch all active RSVPs (confirmed + waitlisted) without the tenant query filter
            // because this may be called from an admin context where the org is the same tenant.
            var activeRsvps = await _context.ActivityRsvps
                .Where(r => r.ActivityId == activity.Id
                         && (r.Status == RsvpStatus.Confirmed || r.Status == RsvpStatus.Waitlisted))
                .ToListAsync();

            if (!activeRsvps.Any()) return;

            var userIds = activeRsvps.Select(r => r.UserId).Distinct().ToList();

            // Load user info
            var users    = await _context.Users.Where(u => userIds.Contains(u.Id)).ToListAsync();
            var profiles = await _context.PlayerProfiles.Where(p => userIds.Contains(p.UserId)).ToListAsync();

            var userMap    = users.ToDictionary(u => u.Id);
            var profileMap = profiles.ToDictionary(p => p.UserId);

            var now = DateTime.UtcNow;

            foreach (var rsvp in activeRsvps)
            {
                try
                {
                    var user    = userMap.GetValueOrDefault(rsvp.UserId);
                    var profile = profileMap.GetValueOrDefault(rsvp.UserId);

                    var email = user?.Email ?? string.Empty;
                    var name  = profile is not null && !string.IsNullOrWhiteSpace(profile.DisplayName)
                        ? profile.DisplayName
                        : profile is not null
                            ? $"{profile.FirstName} {profile.LastName}".Trim()
                            : user?.UserName ?? "Player";

                    // Email
                    if (org is not null && !string.IsNullOrWhiteSpace(email))
                    {
                        _email.SendActivityCancelledToPlayerAsync(activity, org, email, name);
                    }

                    // In-app notification
                    _context.AppNotifications.Add(new AppNotification
                    {
                        UserId    = rsvp.UserId,
                        Type      = AppNotificationType.ActivityCancelled,
                        Title     = $"Activity Cancelled \u2014 {activity.Name}",
                        Body      = $"{activity.Name} scheduled on {activity.Date:MMMM d, yyyy} has been cancelled. We apologise for the inconvenience.",
                        IsRead    = false,
                        CreatedAt = now,
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to notify player {UserId} of activity cancellation {ActivityId}",
                        rsvp.UserId, activity.Id);
                }
            }

            // Save all notifications in one batch
            await _context.SaveChangesAsync();

            // Telegram admin alert
            var activeTelegram = telegram ?? _telegram;
            if (activeTelegram is not null && org is not null)
            {
                activeTelegram.SendActivityCancelledAlertAsync(activity, org);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send bulk cancellation notifications for activity {ActivityId}", activity.Id);
        }
    }

    /// <summary>
    /// Checks whether the user is an administrator or staff member of the specified organization (or platform admin).
    /// </summary>

    public async Task<bool> IsUserAdminForOrganizationAsync(int organizationId, string userId)
    {
        if (string.IsNullOrEmpty(userId)) return false;

        var isMember = await _context.OrganizationMembers
            .IgnoreQueryFilters()
            .AnyAsync(m => m.OrganizationId == organizationId && m.UserId == userId);

        if (isMember) return true;

        var isPlatformAdmin = await (
            from ur in _context.UserRoles
            join r in _context.Roles on ur.RoleId equals r.Id
            where ur.UserId == userId && r.Name == PlatformRoles.PlatformAdmin
            select ur.UserId
        ).AnyAsync();

        return isPlatformAdmin;
    }

    /// <summary>
    /// Returns the subset of activity IDs for which the specified user is an administrator.
    /// </summary>
    public async Task<HashSet<int>> GetAdminActivityIdsAsync(IEnumerable<int> activityIds, string userId)
    {
        if (string.IsNullOrEmpty(userId)) return [];

        var ids = activityIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        var isPlatformAdmin = await (
            from ur in _context.UserRoles
            join r in _context.Roles on ur.RoleId equals r.Id
            where ur.UserId == userId && r.Name == PlatformRoles.PlatformAdmin
            select ur.UserId
        ).AnyAsync();

        if (isPlatformAdmin)
        {
            return ids.ToHashSet();
        }

        var memberOrgIds = await _context.OrganizationMembers
            .IgnoreQueryFilters()
            .Where(m => m.UserId == userId)
            .Select(m => m.OrganizationId)
            .ToListAsync();

        if (memberOrgIds.Count == 0) return [];

        var adminActivityIds = await _context.Activities
            .IgnoreQueryFilters()
            .Where(a => ids.Contains(a.Id) && memberOrgIds.Contains(a.OrganizationId))
            .Select(a => a.Id)
            .ToListAsync();

        return adminActivityIds.ToHashSet();
    }
}

// ── Supporting Record Types ────────────────────────────────────────────────

public record RsvpResult(bool Success, string Message, ActivityRsvp? Rsvp, bool IsWaitlisted);

public record CancelRsvpResult(bool Success, string Message, ActivityRsvp? PromotedRsvp);

public record RsvpPlayerItem(
    int RsvpId,
    string UserId,
    string PlayerName,
    string Email,
    string? Mobile,
    PlayerSkillLevel? SkillLevel,
    string? AvatarUrl,
    RsvpStatus Status,
    int? WaitlistPosition,
    DateTime CreatedAt,
    DateTime? CheckedInAt = null,
    CheckInMethod? CheckInMethod = null);

public record ActivityRosterResult(
    Activity Activity,
    List<RsvpPlayerItem> ConfirmedPlayers,
    List<RsvpPlayerItem> WaitlistedPlayers,
    List<RsvpPlayerItem> CancelledPlayers,
    int MaxCapacity,
    int ConfirmedCount,
    int WaitlistCount,
    int CheckedInCount = 0,
    int NoShowCount = 0);

public record UserActivityItem(
    int RsvpId,
    int ActivityId,
    string ActivityName,
    string OrganizationName,
    string OrganizationSlug,
    DateOnly Date,
    TimeSpan StartTime,
    TimeSpan EndTime,
    ActivityFormat Format,
    ActivitySkillLevel SkillLevel,
    decimal PricePerPlayer,
    string CourtNames,
    RsvpStatus Status,
    int? WaitlistPosition,
    DateTime CreatedAt,
    DateTime? CheckedInAt = null,
    CheckInMethod? CheckInMethod = null);
