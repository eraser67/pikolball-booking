using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

/// <summary>
/// Phase 38: Type of entity being checked in.
/// </summary>
public enum CheckInItemType
{
    ActivityRsvp,
    CourtBooking
}

/// <summary>
/// Result of a check-in, no-show, or undo action.
/// </summary>
public record CheckInResult(
    bool Success,
    string Message,
    CheckInItemType ItemType,
    string? Title = null,
    string? PlayerName = null,
    DateTime? CheckedInAt = null,
    RsvpStatus? RsvpStatus = null,
    bool? BookingIsNoShow = null,
    int? ItemId = null,
    string? Reference = null);

/// <summary>
/// Attendance statistics for a player (tenant-scoped or platform-wide).
/// </summary>
public record PlayerAttendanceStats(
    int TotalActivities,
    int CheckedInCount,
    int NoShowCount,
    int PendingCount,
    double AttendanceRate);

/// <summary>
/// Today's aggregated check-in queue for venue staff.
/// </summary>
public record TodayCheckInFeed(
    DateOnly Date,
    int TotalExpected,
    int TotalCheckedIn,
    int TotalNoShow,
    int TotalPending,
    List<TodayActivityItem> Activities,
    List<TodayBookingItem> Bookings);

public record TodayActivityItem(
    int ActivityId,
    string Name,
    TimeSpan StartTime,
    TimeSpan EndTime,
    string Courts,
    int TotalRsvps,
    int CheckedInCount,
    int NoShowCount,
    List<TodayActivityPlayerItem> Players);

public record TodayActivityPlayerItem(
    int RsvpId,
    string UserId,
    string PlayerName,
    string Email,
    string? Mobile,
    string? AvatarUrl,
    PlayerSkillLevel? SkillLevel,
    RsvpStatus Status,
    DateTime? CheckedInAt,
    CheckInMethod? CheckInMethod,
    bool IsGuest = false);

public record TodayBookingItem(
    int BookingId,
    string BookingReference,
    string CustomerName,
    string CustomerEmail,
    string CustomerPhone,
    string CourtName,
    DateOnly Date,
    TimeSpan StartTime,
    TimeSpan EndTime,
    decimal Price,
    BookingStatus Status,
    bool IsCheckedIn,
    DateTime? CheckedInAt,
    CheckInMethod? CheckInMethod,
    bool IsNoShow);

/// <summary>
/// Phase 38: Service for managing player attendance, check-ins, and no-shows.
/// </summary>
public interface IPlayerCheckInService
{
    Task<CheckInResult> CheckInRsvpAsync(int rsvpId, CheckInMethod method, string staffUserId);
    Task<CheckInResult> MarkRsvpNoShowAsync(int rsvpId, string staffUserId);
    Task<CheckInResult> UndoRsvpCheckInAsync(int rsvpId, string staffUserId);

    Task<CheckInResult> CheckInBookingAsync(string bookingReference, CheckInMethod method, string staffUserId);
    Task<CheckInResult> MarkBookingNoShowAsync(string bookingReference, string staffUserId);
    Task<CheckInResult> UndoBookingCheckInAsync(string bookingReference, string staffUserId);

    Task<CheckInResult> ProcessQrCodeAsync(string qrPayload, string staffUserId, int? organizationId = null);
    Task<PlayerAttendanceStats> GetPlayerAttendanceStatsAsync(string userId, int? organizationId = null);
    Task<TodayCheckInFeed> GetTodayCheckInFeedAsync(int? organizationId, DateOnly? date = null);
}
