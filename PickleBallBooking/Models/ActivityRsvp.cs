using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace PickleBallBooking.Models;

/// <summary>
/// Phase 34: a player's RSVP or waitlist entry for an organized Activity.
///
/// Multi-tenant: scoped by OrganizationId — all reads/writes go through the EF
/// global query filter.
/// </summary>
public class ActivityRsvp
{
    public int Id { get; set; }

    /// <summary>Tenant owner. Set automatically by the EF write guard.</summary>
    public int OrganizationId { get; set; }

    public int ActivityId { get; set; }
    public Activity? Activity { get; set; }

    /// <summary>Foreign key to AspNetUsers.Id.</summary>
    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    public IdentityUser? User { get; set; }

    public RsvpStatus Status { get; set; } = RsvpStatus.Confirmed;

    /// <summary>
    /// Position on the waitlist (1-based: 1 = next in line).
    /// Null when confirmed, cancelled, or not on waitlist.
    /// </summary>
    public int? WaitlistPosition { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    /// <summary>Phase 38: Timestamp when the player was checked in on the day of the activity.</summary>
    public DateTime? CheckedInAt { get; set; }

    /// <summary>Phase 38: Identity user ID of staff who checked the player in.</summary>
    [MaxLength(450)]
    public string? CheckedInByUserId { get; set; }

    /// <summary>Phase 38: Method used to check in (QR Scan, Admin Dashboard, Manual).</summary>
    public CheckInMethod? CheckInMethod { get; set; }

    /// <summary>Phase 39: Court assignment for this RSVP if assigned.</summary>
    public ActivityCourtAssignment? CourtAssignment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

