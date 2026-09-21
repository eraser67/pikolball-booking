namespace PickleBallBooking.Models;

/// <summary>
/// Represents the availability status of a TimeSlot for a specific Court.
/// Allows each court to independently enable/disable time slots for maintenance or other reasons.
/// </summary>
public class CourtTimeSlot
{
    public int Id { get; set; }

    public int OrganizationId { get; set; }

    public int CourtId { get; set; }

    public int TimeSlotId { get; set; }

    /// <summary>
    /// Availability status of this time slot for this court.
    /// Active = available for booking
    /// Maintenance = unavailable, cannot be booked
    /// </summary>
    public CourtTimeSlotStatus AvailabilityStatus { get; set; } = CourtTimeSlotStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Court Court { get; set; } = null!;

    public TimeSlot TimeSlot { get; set; } = null!;
}

/// <summary>
/// Enum for CourtTimeSlot availability status.
/// </summary>
public enum CourtTimeSlotStatus
{
    Active = 0,
    Maintenance = 1
}
