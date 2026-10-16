using System.ComponentModel.DataAnnotations;

namespace PickleBallBooking.Models;

/// <summary>
/// Represents a tenant-specific override (Active/Inactive status, custom times, or deletion) for a master TimeSlot.
/// If no record exists for an organization and time slot, the slot status defaults to Active.
/// </summary>
public class TenantTimeSlot
{
    public int Id { get; set; }

    public int OrganizationId { get; set; }

    public int TimeSlotId { get; set; }

    public TimeSlotStatus Status { get; set; } = TimeSlotStatus.Active;

    public TimeSpan? CustomStartTime { get; set; }

    public TimeSpan? CustomEndTime { get; set; }

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Organization Organization { get; set; } = null!;

    public TimeSlot TimeSlot { get; set; } = null!;
}
