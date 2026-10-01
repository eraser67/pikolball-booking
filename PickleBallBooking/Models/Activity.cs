using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickleBallBooking.Models;

/// <summary>
/// Phase 33: a tenant-owned organised pickleball activity (Open Play, Social Play, etc.).
///
/// Activities are separate from the court booking engine. The venue operator
/// allocates courts to the activity; individual players do NOT create court bookings.
///
/// Multi-tenant: scoped by OrganizationId — all reads/writes go through the EF
/// global query filter (same as Court, Booking, etc.).
///
/// Many-to-many with Court via ActivityCourt join table.
/// </summary>
public class Activity
{
    public int Id { get; set; }

    /// <summary>Tenant owner. Set automatically by the EF write guard.</summary>
    public int OrganizationId { get; set; }

    // ── Basic Info ────────────────────────────────────────────────────────
    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public ActivityFormat Format { get; set; } = ActivityFormat.OpenPlay;

    // ── Schedule ──────────────────────────────────────────────────────────
    public DateOnly Date { get; set; }

    [Column(TypeName = "time without time zone")]
    public TimeSpan StartTime { get; set; }

    [Column(TypeName = "time without time zone")]
    public TimeSpan EndTime { get; set; }

    // ── Participation ─────────────────────────────────────────────────────
    public ActivitySkillLevel SkillLevel { get; set; } = ActivitySkillLevel.Open;

    /// <summary>Maximum number of confirmed players. 0 = unlimited.</summary>
    public int MaxCapacity { get; set; } = 0;

    // ── Pricing ───────────────────────────────────────────────────────────
    /// <summary>Price per player in Philippine Peso. 0 = free.</summary>
    [Column(TypeName = "numeric(8,2)")]
    public decimal PricePerPlayer { get; set; } = 0;

    // ── Registration Window ───────────────────────────────────────────────
    /// <summary>When null, the admin opens registration manually.</summary>
    public DateTime? RegistrationOpensAt { get; set; }

    /// <summary>When null, registration stays open until capacity or admin close.</summary>
    public DateTime? RegistrationClosesAt { get; set; }

    // ── Status ────────────────────────────────────────────────────────────
    public ActivityStatus Status { get; set; } = ActivityStatus.Draft;

    // ── Phase 35: Recurring Series back-reference ─────────────────────────
    /// <summary>
    /// Null for standalone activities. When set, this Activity was generated
    /// from the given <see cref="ActivitySeries"/> recurring template.
    /// </summary>
    public int? SeriesId { get; set; }

    /// <summary>Navigation to the parent series. Null for standalone activities.</summary>
    public ActivitySeries? Series { get; set; }

    // ── Timestamps ────────────────────────────────────────────────────────
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ── Navigation ────────────────────────────────────────────────────────
    /// <summary>Courts assigned to this activity via the ActivityCourt join table.</summary>
    public ICollection<ActivityCourt> ActivityCourts { get; set; } = new List<ActivityCourt>();

    /// <summary>Phase 34: RSVPs and waitlist registrations for this activity.</summary>
    public ICollection<ActivityRsvp> Rsvps { get; set; } = new List<ActivityRsvp>();

    // ── Registration State Helpers ─────────────────────────────────────────

    /// <summary>
    /// Returns true if registration has passed its closing window or was manually closed.
    /// </summary>
    public bool IsRegistrationClosed(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        if (Status is ActivityStatus.RegistrationClosed or ActivityStatus.Completed or ActivityStatus.Cancelled)
            return true;

        if (RegistrationClosesAt.HasValue && now > RegistrationClosesAt.Value)
            return true;

        return false;
    }

    /// <summary>
    /// Returns true if registration is open for joiners or waitlist.
    /// Evaluates explicit status and automated time window.
    /// </summary>
    public bool IsRegistrationOpen(DateTime? nowUtc = null)
    {
        if (IsRegistrationClosed(nowUtc))
            return false;

        if (Status is ActivityStatus.Draft)
            return false;

        if (Status is ActivityStatus.RegistrationOpen or ActivityStatus.Full)
            return true;

        if (Status is ActivityStatus.Published)
        {
            var now = nowUtc ?? DateTime.UtcNow;
            if (RegistrationOpensAt.HasValue && now >= RegistrationOpensAt.Value)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if the activity is published but registration has not opened yet.
    /// </summary>
    public bool IsRegistrationPending(DateTime? nowUtc = null)
    {
        if (IsRegistrationClosed(nowUtc))
            return false;

        if (Status == ActivityStatus.Published)
        {
            var now = nowUtc ?? DateTime.UtcNow;
            if (!RegistrationOpensAt.HasValue || now < RegistrationOpensAt.Value)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Derives the effective activity status accounting for automated opening/closing windows.
    /// </summary>
    public ActivityStatus GetEffectiveStatus(DateTime? nowUtc = null)
    {
        if (Status is ActivityStatus.Draft or ActivityStatus.InProgress or ActivityStatus.Completed or ActivityStatus.Cancelled)
            return Status;

        if (IsRegistrationClosed(nowUtc))
            return ActivityStatus.RegistrationClosed;

        if (Status == ActivityStatus.Full)
            return ActivityStatus.Full;

        if (IsRegistrationOpen(nowUtc))
            return ActivityStatus.RegistrationOpen;

        if (IsRegistrationPending(nowUtc))
            return ActivityStatus.Published;

        return Status;
    }
}
