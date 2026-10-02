using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public class CreateGuestPlayerDto
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public PlayerSkillLevel SkillLevel { get; set; } = PlayerSkillLevel.Beginner;
    public PlayingHand PlayingHand { get; set; } = PlayingHand.Right;
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? AdminNotes { get; set; }
}

public class UpdateGuestPlayerDto
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public PlayerSkillLevel SkillLevel { get; set; } = PlayerSkillLevel.Beginner;
    public PlayingHand PlayingHand { get; set; } = PlayingHand.Right;
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? AdminNotes { get; set; }
}

public class VenuePlayerListItemDto
{
    public string UserId { get; set; } = string.Empty;
    public int PlayerProfileId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public PlayerSkillLevel SkillLevel { get; set; }
    public PlayingHand PlayingHand { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public bool IsGuest { get; set; }
    public int? CreatedByOrganizationId { get; set; }
    public string? AdminNotes { get; set; }
    public int ActivitiesAttended { get; set; }
    public int MatchesPlayed { get; set; }
    public DateTime? LastActiveDate { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AddPlayerToActivityResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? RsvpId { get; set; }
    public RsvpStatus Status { get; set; }
    public int? WaitlistPosition { get; set; }
}

public interface ITenantPlayerService
{
    /// <summary>
    /// Registers a walk-in / guest player for the organization without requiring an account login.
    /// </summary>
    Task<PlayerProfile> CreateGuestPlayerAsync(int orgId, CreateGuestPlayerDto dto);

    /// <summary>
    /// Retrieves all players associated with this venue (both tenant-created guest players
    /// and registered platform players who have participated in venue activities or bookings).
    /// </summary>
    Task<List<VenuePlayerListItemDto>> GetVenuePlayersAsync(int orgId, string? search = null, bool? onlyGuests = null);

    /// <summary>
    /// Retrieves a guest player created by this tenant.
    /// </summary>
    Task<PlayerProfile?> GetGuestPlayerAsync(int orgId, string userId);

    /// <summary>
    /// Updates details of a tenant-managed guest player.
    /// </summary>
    Task<bool> UpdateGuestPlayerAsync(int orgId, string userId, UpdateGuestPlayerDto dto);

    /// <summary>
    /// Directly adds an existing player (registered or walk-in) to an activity.
    /// </summary>
    Task<AddPlayerToActivityResult> AddPlayerToActivityAsync(int orgId, int activityId, string userId, bool bypassCapacity = false);

    /// <summary>
    /// Atomically registers a new walk-in player and immediately adds them to the activity.
    /// </summary>
    Task<AddPlayerToActivityResult> RegisterAndAddToActivityAsync(int orgId, int activityId, CreateGuestPlayerDto dto, bool bypassCapacity = false);
}
