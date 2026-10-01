using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public record CourtAssignedPlayerDto(
    int RsvpId,
    string UserId,
    string PlayerName,
    string? AvatarUrl,
    PlayerSkillLevel SkillLevel,
    bool IsCheckedIn,
    int? CourtId,
    string? CourtName,
    int SlotNumber,
    DateTime? AssignedAt
);

public record CourtGroupDto(
    int CourtId,
    string CourtName,
    int SlotCapacity,
    List<CourtAssignedPlayerDto> Players
);

public record ActivityCourtAssignmentOverviewDto(
    int ActivityId,
    string ActivityName,
    DateOnly Date,
    TimeSpan StartTime,
    TimeSpan EndTime,
    ActivityFormat Format,
    ActivityStatus Status,
    bool AreCourtAssignmentsLocked,
    DateTime? CourtAssignmentsLockedAt,
    string? CourtAssignmentsLockedByUserId,
    List<CourtGroupDto> Courts,
    List<CourtAssignedPlayerDto> UnassignedPlayers,
    int TotalConfirmedCount,
    int TotalAssignedCount
);

public record CourtAssignmentResult(
    bool Success,
    string Message,
    int? AssignmentId = null
);

public interface ICourtAssignmentService
{
    Task<ActivityCourtAssignmentOverviewDto?> GetOverviewAsync(int activityId);

    Task<CourtAssignmentResult> AssignPlayerAsync(int activityId, int rsvpId, int courtId, int? slotNumber = null, string? adminUserId = null);

    Task<CourtAssignmentResult> MovePlayerAsync(int activityId, int rsvpId, int targetCourtId, int? newSlotNumber = null, string? adminUserId = null);

    Task<CourtAssignmentResult> UnassignPlayerAsync(int activityId, int rsvpId, string? adminUserId = null);

    Task<CourtAssignmentResult> AutoAssignAsync(int activityId, string? adminUserId = null);

    Task<CourtAssignmentResult> SkillBasedGroupAsync(int activityId, string? adminUserId = null);

    Task<CourtAssignmentResult> RebalanceAsync(int activityId, string? adminUserId = null);

    Task<CourtAssignmentResult> ClearAssignmentsAsync(int activityId, string? adminUserId = null);

    Task<CourtAssignmentResult> SetLockAsync(int activityId, bool locked, string? adminUserId = null);

    Task<CourtAssignedPlayerDto?> GetPlayerCourtAssignmentAsync(int activityId, string userId);
}
