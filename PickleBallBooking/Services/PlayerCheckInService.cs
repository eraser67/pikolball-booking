using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

/// <summary>
/// Phase 38: Concrete implementation of IPlayerCheckInService for managing player attendance,
/// QR code processing, check-in feeds, and no-show statistics.
/// </summary>
public class PlayerCheckInService : IPlayerCheckInService
{
    private readonly ApplicationDbContext _context;
    private readonly AppNotificationService _notifications;
    private readonly ICourtImageStorage _storage;
    private readonly ILogger<PlayerCheckInService> _logger;

    public PlayerCheckInService(
        ApplicationDbContext context,
        AppNotificationService notifications,
        ICourtImageStorage storage,
        ILogger<PlayerCheckInService> logger)
    {
        _context       = context;
        _notifications = notifications;
        _storage       = storage;
        _logger        = logger;
    }

    // ── Activity RSVP Check-In ────────────────────────────────────────────────

    public async Task<CheckInResult> CheckInRsvpAsync(int rsvpId, CheckInMethod method, string staffUserId)
    {
        var rsvp = await _context.ActivityRsvps
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == rsvpId);

        if (rsvp is null)
        {
            return new CheckInResult(false, "RSVP record not found.", CheckInItemType.ActivityRsvp, ItemId: rsvpId);
        }

        var activity = await _context.Activities
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == rsvp.ActivityId);

        if (rsvp.Status == RsvpStatus.Cancelled)
        {
            return new CheckInResult(false, "Cannot check in a cancelled RSVP.", CheckInItemType.ActivityRsvp, activity?.Name, ItemId: rsvpId);
        }

        if (rsvp.Status == RsvpStatus.Waitlisted)
        {
            return new CheckInResult(false, "Player is currently on the waitlist. Manually promote them to confirmed before checking in.", CheckInItemType.ActivityRsvp, activity?.Name, ItemId: rsvpId);
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == rsvp.UserId);
        var playerName = await ResolvePlayerNameAsync(rsvp.UserId, user?.UserName);

        if (rsvp.Status == RsvpStatus.CheckedIn)
        {
            return new CheckInResult(false, $"{playerName} is already checked in.", CheckInItemType.ActivityRsvp, activity?.Name, playerName, rsvp.CheckedInAt, rsvp.Status, ItemId: rsvpId);
        }

        rsvp.Status            = RsvpStatus.CheckedIn;
        rsvp.CheckedInAt       = DateTime.UtcNow;
        rsvp.CheckedInByUserId = staffUserId;
        rsvp.CheckInMethod     = method;
        rsvp.UpdatedAt         = DateTime.UtcNow;

        var prevGuard = _context.SuppressTenantWriteGuard;
        try
        {
            _context.SuppressTenantWriteGuard = true;
            await _context.SaveChangesAsync();
        }
        finally
        {
            _context.SuppressTenantWriteGuard = prevGuard;
        }

        // Send in-app notification to player
        try
        {
            if (activity is not null)
            {
                await _notifications.CreateAsync(
                    rsvp.UserId,
                    AppNotificationType.ActivityCheckedIn,
                    $"Checked In — {activity.Name}",
                    $"You are checked in for {activity.Name} on {activity.Date:MMMM d, yyyy}. Have a great game!",
                    actionUrl: "/Notifications");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send check-in notification for RSVP {RsvpId}", rsvp.Id);
        }

        return new CheckInResult(
            true,
            $"{playerName} successfully checked in!",
            CheckInItemType.ActivityRsvp,
            activity?.Name,
            playerName,
            rsvp.CheckedInAt,
            rsvp.Status,
            ItemId: rsvp.Id);
    }

    public async Task<CheckInResult> MarkRsvpNoShowAsync(int rsvpId, string staffUserId)
    {
        var rsvp = await _context.ActivityRsvps
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == rsvpId);

        if (rsvp is null)
        {
            return new CheckInResult(false, "RSVP record not found.", CheckInItemType.ActivityRsvp, ItemId: rsvpId);
        }

        var activity = await _context.Activities
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == rsvp.ActivityId);

        if (rsvp.Status == RsvpStatus.Cancelled || rsvp.Status == RsvpStatus.Waitlisted)
        {
            return new CheckInResult(false, "Cannot mark a cancelled or waitlisted player as no-show.", CheckInItemType.ActivityRsvp, activity?.Name, ItemId: rsvpId);
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == rsvp.UserId);
        var playerName = await ResolvePlayerNameAsync(rsvp.UserId, user?.UserName);

        rsvp.Status            = RsvpStatus.NoShow;
        rsvp.CheckedInAt       = null;
        rsvp.CheckedInByUserId = staffUserId;
        rsvp.CheckInMethod     = null;
        rsvp.UpdatedAt         = DateTime.UtcNow;

        var prevGuard = _context.SuppressTenantWriteGuard;
        try
        {
            _context.SuppressTenantWriteGuard = true;
            await _context.SaveChangesAsync();
        }
        finally
        {
            _context.SuppressTenantWriteGuard = prevGuard;
        }

        return new CheckInResult(
            true,
            $"{playerName} marked as No-Show.",
            CheckInItemType.ActivityRsvp,
            activity?.Name,
            playerName,
            null,
            rsvp.Status,
            ItemId: rsvp.Id);
    }

    public async Task<CheckInResult> UndoRsvpCheckInAsync(int rsvpId, string staffUserId)
    {
        var rsvp = await _context.ActivityRsvps
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == rsvpId);

        if (rsvp is null)
        {
            return new CheckInResult(false, "RSVP record not found.", CheckInItemType.ActivityRsvp, ItemId: rsvpId);
        }

        var activity = await _context.Activities
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == rsvp.ActivityId);

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == rsvp.UserId);
        var playerName = await ResolvePlayerNameAsync(rsvp.UserId, user?.UserName);

        rsvp.Status            = RsvpStatus.Confirmed;
        rsvp.CheckedInAt       = null;
        rsvp.CheckedInByUserId = null;
        rsvp.CheckInMethod     = null;
        rsvp.UpdatedAt         = DateTime.UtcNow;

        var prevGuard = _context.SuppressTenantWriteGuard;
        try
        {
            _context.SuppressTenantWriteGuard = true;
            await _context.SaveChangesAsync();
        }
        finally
        {
            _context.SuppressTenantWriteGuard = prevGuard;
        }

        return new CheckInResult(
            true,
            $"Check-in undone for {playerName}. Status reset to Confirmed.",
            CheckInItemType.ActivityRsvp,
            activity?.Name,
            playerName,
            null,
            rsvp.Status,
            ItemId: rsvp.Id);
    }

    // ── Court Booking Check-In ────────────────────────────────────────────────

    public async Task<CheckInResult> CheckInBookingAsync(string bookingReference, CheckInMethod method, string staffUserId)
    {
        var booking = await _context.Bookings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.BookingReference == bookingReference);

        if (booking is null)
        {
            return new CheckInResult(false, $"Booking '{bookingReference}' not found.", CheckInItemType.CourtBooking, Reference: bookingReference);
        }

        var court = booking.CourtId > 0
            ? await _context.Courts.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == booking.CourtId)
            : null;

        if (booking.BookingStatus == BookingStatus.Cancelled)
        {
            return new CheckInResult(false, "Cannot check in a cancelled booking.", CheckInItemType.CourtBooking, $"{court?.Name ?? "Court"} Booking", booking.CustomerName, Reference: bookingReference);
        }

        if (booking.CheckedInAt.HasValue && !booking.IsNoShow)
        {
            return new CheckInResult(false, $"Booking {bookingReference} ({booking.CustomerName}) is already checked in.", CheckInItemType.CourtBooking, $"{court?.Name ?? "Court"} Booking", booking.CustomerName, booking.CheckedInAt, Reference: bookingReference);
        }

        booking.CheckedInAt       = DateTime.UtcNow;
        booking.CheckedInByUserId = staffUserId;
        booking.CheckInMethod     = method;
        booking.IsNoShow          = false;
        booking.UpdatedAt         = DateTime.UtcNow;

        var prevGuard = _context.SuppressTenantWriteGuard;
        try
        {
            _context.SuppressTenantWriteGuard = true;
            await _context.SaveChangesAsync();
        }
        finally
        {
            _context.SuppressTenantWriteGuard = prevGuard;
        }

        return new CheckInResult(
            true,
            $"Booking {booking.BookingReference} ({booking.CustomerName}) checked in successfully!",
            CheckInItemType.CourtBooking,
            $"{court?.Name ?? "Court"} Booking",
            booking.CustomerName,
            booking.CheckedInAt,
            Reference: booking.BookingReference,
            ItemId: booking.Id);
    }

    public async Task<CheckInResult> MarkBookingNoShowAsync(string bookingReference, string staffUserId)
    {
        var booking = await _context.Bookings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.BookingReference == bookingReference);

        if (booking is null)
        {
            return new CheckInResult(false, $"Booking '{bookingReference}' not found.", CheckInItemType.CourtBooking, Reference: bookingReference);
        }

        var court = booking.CourtId > 0
            ? await _context.Courts.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == booking.CourtId)
            : null;

        if (booking.BookingStatus == BookingStatus.Cancelled)
        {
            return new CheckInResult(false, "Cannot mark a cancelled booking as no-show.", CheckInItemType.CourtBooking, Reference: bookingReference);
        }

        booking.IsNoShow          = true;
        booking.CheckedInAt       = null;
        booking.CheckedInByUserId = staffUserId;
        booking.CheckInMethod     = null;
        booking.UpdatedAt         = DateTime.UtcNow;

        var prevGuard = _context.SuppressTenantWriteGuard;
        try
        {
            _context.SuppressTenantWriteGuard = true;
            await _context.SaveChangesAsync();
        }
        finally
        {
            _context.SuppressTenantWriteGuard = prevGuard;
        }

        return new CheckInResult(
            true,
            $"Booking {booking.BookingReference} ({booking.CustomerName}) marked as No-Show.",
            CheckInItemType.CourtBooking,
            $"{court?.Name ?? "Court"} Booking",
            booking.CustomerName,
            null,
            BookingIsNoShow: true,
            Reference: booking.BookingReference,
            ItemId: booking.Id);
    }

    public async Task<CheckInResult> UndoBookingCheckInAsync(string bookingReference, string staffUserId)
    {
        var booking = await _context.Bookings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.BookingReference == bookingReference);

        if (booking is null)
        {
            return new CheckInResult(false, $"Booking '{bookingReference}' not found.", CheckInItemType.CourtBooking, Reference: bookingReference);
        }

        var court = booking.CourtId > 0
            ? await _context.Courts.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == booking.CourtId)
            : null;

        booking.CheckedInAt       = null;
        booking.CheckedInByUserId = null;
        booking.CheckInMethod     = null;
        booking.IsNoShow          = false;
        booking.UpdatedAt         = DateTime.UtcNow;

        var prevGuard = _context.SuppressTenantWriteGuard;
        try
        {
            _context.SuppressTenantWriteGuard = true;
            await _context.SaveChangesAsync();
        }
        finally
        {
            _context.SuppressTenantWriteGuard = prevGuard;
        }

        return new CheckInResult(
            true,
            $"Check-in undone for booking {booking.BookingReference}.",
            CheckInItemType.CourtBooking,
            $"{court?.Name ?? "Court"} Booking",
            booking.CustomerName,
            null,
            BookingIsNoShow: false,
            Reference: booking.BookingReference,
            ItemId: booking.Id);
    }

    // ── QR Code Processing ───────────────────────────────────────────────────

    public async Task<CheckInResult> ProcessQrCodeAsync(string qrPayload, string staffUserId, int? organizationId = null)
    {
        if (string.IsNullOrWhiteSpace(qrPayload))
        {
            return new CheckInResult(false, "Scanned QR code payload is empty.", CheckInItemType.ActivityRsvp);
        }

        var trimmed = qrPayload.Trim().Trim('"', '\'');

        // Format 1: Explicit RSVP payload: "RSVP:123" or "RSVP:123:token"
        if (trimmed.StartsWith("RSVP:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmed.Substring(5).Split(':');
            if (int.TryParse(parts[0], out var rsvpId))
            {
                var rsvp = await _context.ActivityRsvps
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(r => r.Id == rsvpId);

                if (rsvp is null)
                {
                    return new CheckInResult(false, $"RSVP #{rsvpId} not found.", CheckInItemType.ActivityRsvp, ItemId: rsvpId);
                }

                if (organizationId.HasValue && rsvp.OrganizationId != organizationId.Value)
                {
                    return new CheckInResult(false, "This RSVP does not belong to this venue.", CheckInItemType.ActivityRsvp, ItemId: rsvpId);
                }

                return await CheckInRsvpAsync(rsvpId, CheckInMethod.QrScan, staffUserId);
            }
        }

        // Format 2: Explicit Booking payload: "BOOKING:REF"
        if (trimmed.StartsWith("BOOKING:", StringComparison.OrdinalIgnoreCase))
        {
            var bRef = trimmed.Substring(8).Trim();
            var booking = await _context.Bookings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(b => b.BookingReference == bRef);

            if (booking is null)
            {
                return new CheckInResult(false, $"Booking '{bRef}' not found.", CheckInItemType.CourtBooking, Reference: bRef);
            }

            if (organizationId.HasValue && booking.OrganizationId != organizationId.Value)
            {
                return new CheckInResult(false, "This booking does not belong to this venue.", CheckInItemType.CourtBooking, Reference: bRef);
            }

            return await CheckInBookingAsync(bRef, CheckInMethod.QrScan, staffUserId);
        }

        // Format 3: Raw Booking Reference (check Bookings first)
        var matchedBooking = await _context.Bookings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.BookingReference == trimmed);

        if (matchedBooking is not null)
        {
            if (organizationId.HasValue && matchedBooking.OrganizationId != organizationId.Value)
            {
                return new CheckInResult(false, "This booking does not belong to this venue.", CheckInItemType.CourtBooking, Reference: trimmed);
            }

            return await CheckInBookingAsync(trimmed, CheckInMethod.QrScan, staffUserId);
        }

        // Format 4: Integer ID -> test RSVP ID
        if (int.TryParse(trimmed, out var intId))
        {
            var matchedRsvp = await _context.ActivityRsvps
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.Id == intId);

            if (matchedRsvp is not null)
            {
                if (organizationId.HasValue && matchedRsvp.OrganizationId != organizationId.Value)
                {
                    return new CheckInResult(false, "This RSVP does not belong to this venue.", CheckInItemType.ActivityRsvp, ItemId: intId);
                }

                return await CheckInRsvpAsync(intId, CheckInMethod.QrScan, staffUserId);
            }
        }

        return new CheckInResult(false, $"Unrecognized QR code format: '{trimmed}'.", CheckInItemType.ActivityRsvp);
    }

    // ── Player Attendance History ─────────────────────────────────────────────

    public async Task<PlayerAttendanceStats> GetPlayerAttendanceStatsAsync(string userId, int? organizationId = null)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return new PlayerAttendanceStats(0, 0, 0, 0, 0.0);
        }

        var query = _context.ActivityRsvps
            .IgnoreQueryFilters()
            .Where(r => r.UserId == userId && r.Status != RsvpStatus.Cancelled && r.Status != RsvpStatus.Waitlisted);

        if (organizationId.HasValue)
        {
            query = query.Where(r => r.OrganizationId == organizationId.Value);
        }

        var rsvps = await query.ToListAsync();

        var total = rsvps.Count;
        var checkedIn = rsvps.Count(r => r.Status == RsvpStatus.CheckedIn);
        var noShow    = rsvps.Count(r => r.Status == RsvpStatus.NoShow);
        var pending   = rsvps.Count(r => r.Status == RsvpStatus.Confirmed);

        var denominator = checkedIn + noShow;
        var rate = denominator > 0
            ? Math.Round(((double)checkedIn / denominator) * 100.0, 1)
            : 0.0;

        return new PlayerAttendanceStats(total, checkedIn, noShow, pending, rate);
    }

    // ── Today's Check-In Queue ────────────────────────────────────────────────

    public async Task<TodayCheckInFeed> GetTodayCheckInFeedAsync(int? organizationId, DateOnly? date = null)
    {
        var targetDate = date ?? DateOnly.FromDateTime(DateTime.Today);

        // 1. Activities for today
        var actQuery = _context.Activities
            .IgnoreQueryFilters()
            .Include(a => a.ActivityCourts)
                .ThenInclude(ac => ac.Court)
            .Where(a => a.Date == targetDate);

        if (organizationId.HasValue)
        {
            actQuery = actQuery.Where(a => a.OrganizationId == organizationId.Value);
        }

        var activities = await actQuery
            .OrderBy(a => a.StartTime)
            .ToListAsync();

        var actIds = activities.Select(a => a.Id).ToList();

        var rsvps = await _context.ActivityRsvps
            .IgnoreQueryFilters()
            .Where(r => actIds.Contains(r.ActivityId) && (r.Status == RsvpStatus.Confirmed || r.Status == RsvpStatus.CheckedIn || r.Status == RsvpStatus.NoShow))
            .ToListAsync();

        var playerUserIds = rsvps.Select(r => r.UserId).Distinct().ToList();
        var profiles = await _context.PlayerProfiles
            .Where(p => playerUserIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId);

        var users = await _context.Users
            .Where(u => playerUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        var activityItems = activities.Select(a =>
        {
            var actRsvps = rsvps.Where(r => r.ActivityId == a.Id).ToList();
            var courts = a.ActivityCourts.Any()
                ? string.Join(", ", a.ActivityCourts.Select(ac => ac.Court?.Name ?? "?"))
                : "All Courts";

            var playerItems = actRsvps.Select(r =>
            {
                profiles.TryGetValue(r.UserId, out var prof);
                users.TryGetValue(r.UserId, out var u);
                var name = prof is not null && !string.IsNullOrWhiteSpace(prof.DisplayName)
                    ? prof.DisplayName
                    : prof is not null
                        ? $"{prof.FirstName} {prof.LastName}".Trim()
                        : u?.UserName ?? "Player";

                var isGuest = prof?.IsGuest ?? false;
                var rawEmail = u?.Email ?? string.Empty;
                var email = isGuest && rawEmail.EndsWith("@guest.punitbola.tech", StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : rawEmail;

                var avatarUrl = _storage.GetPublicUrl(prof?.AvatarPath);

                return new TodayActivityPlayerItem(
                    r.Id,
                    r.UserId,
                    name,
                    email,
                    prof?.Mobile ?? u?.PhoneNumber,
                    avatarUrl,
                    prof?.SkillLevel,
                    r.Status,
                    r.CheckedInAt,
                    r.CheckInMethod,
                    isGuest);
            }).OrderBy(p => p.Status != RsvpStatus.CheckedIn).ThenBy(p => p.PlayerName).ToList();

            var checkedInCount = actRsvps.Count(r => r.Status == RsvpStatus.CheckedIn);
            var noShowCount    = actRsvps.Count(r => r.Status == RsvpStatus.NoShow);

            return new TodayActivityItem(
                a.Id,
                a.Name,
                a.StartTime,
                a.EndTime,
                courts,
                actRsvps.Count,
                checkedInCount,
                noShowCount,
                playerItems);
        }).ToList();

        // 2. Court Bookings for today
        var bookingQuery = _context.Bookings
            .IgnoreQueryFilters()
            .Where(b => b.BookingDate == targetDate
                     && b.BookingStatus != BookingStatus.Cancelled);

        if (organizationId.HasValue)
        {
            bookingQuery = bookingQuery.Where(b => b.OrganizationId == organizationId.Value);
        }

        var bookings = await bookingQuery
            .OrderBy(b => b.StartTime)
            .ToListAsync();

        var courtIds = bookings.Select(b => b.CourtId).Distinct().ToList();
        var courtsMap = await _context.Courts
            .IgnoreQueryFilters()
            .Where(c => courtIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id);

        var bookingItems = bookings.Select(b =>
        {
            courtsMap.TryGetValue(b.CourtId, out var court);
            return new TodayBookingItem(
                b.Id,
                b.BookingReference,
                b.CustomerName,
                b.CustomerEmail,
                b.CustomerPhone,
                court?.Name ?? "Court",
                b.BookingDate,
                b.StartTime,
                b.EndTime,
                b.Price,
                b.BookingStatus,
                b.CheckedInAt.HasValue && !b.IsNoShow,
                b.CheckedInAt,
                b.CheckInMethod,
                b.IsNoShow);
        }).ToList();

        var totalExpected  = activityItems.Sum(a => a.TotalRsvps) + bookingItems.Count;
        var totalCheckedIn = activityItems.Sum(a => a.CheckedInCount) + bookingItems.Count(b => b.IsCheckedIn);
        var totalNoShow    = activityItems.Sum(a => a.NoShowCount) + bookingItems.Count(b => b.IsNoShow);
        var totalPending   = totalExpected - totalCheckedIn - totalNoShow;

        return new TodayCheckInFeed(
            targetDate,
            totalExpected,
            totalCheckedIn,
            totalNoShow,
            totalPending,
            activityItems,
            bookingItems);
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    private async Task<string> ResolvePlayerNameAsync(string userId, string? defaultUserName)
    {
        var profile = await _context.PlayerProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        if (profile is not null && !string.IsNullOrWhiteSpace(profile.DisplayName))
            return profile.DisplayName;
        if (profile is not null && !string.IsNullOrWhiteSpace(profile.FirstName))
            return $"{profile.FirstName} {profile.LastName}".Trim();
        return defaultUserName ?? "Player";
    }
}
