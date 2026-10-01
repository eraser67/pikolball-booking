using System.ComponentModel.DataAnnotations;

namespace PickleBallBooking.Models;

/// <summary>
/// Phase 39: Court Assignment for an Activity.
/// Assigns a player (via their ActivityRsvp) to a specific Court allocated to the Activity.
/// Multi-tenant: scoped by OrganizationId with EF Core tenant filter.
/// </summary>
public class ActivityCourtAssignment
{
    public int Id { get; set; }

    /// <summary>Tenant owner. Scoped to the organization.</summary>
    public int OrganizationId { get; set; }

    public int ActivityId { get; set; }
    public Activity? Activity { get; set; }

    public int CourtId { get; set; }
    public Court? Court { get; set; }

    public int ActivityRsvpId { get; set; }
    public ActivityRsvp? ActivityRsvp { get; set; }

    /// <summary>Identity user ID of the assigned player.</summary>
    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Slot number or position on the court (1, 2, 3, 4, etc.).</summary>
    public int SlotNumber { get; set; } = 1;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? AssignedByUserId { get; set; }
}
