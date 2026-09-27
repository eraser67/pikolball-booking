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

    // ── Timestamps ────────────────────────────────────────────────────────
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ── Navigation ────────────────────────────────────────────────────────
    /// <summary>Courts assigned to this activity via the ActivityCourt join table.</summary>
    public ICollection<ActivityCourt> ActivityCourts { get; set; } = new List<ActivityCourt>();
}
