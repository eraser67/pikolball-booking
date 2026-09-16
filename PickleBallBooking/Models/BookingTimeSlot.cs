namespace PickleBallBooking.Models;

/// <summary>
/// Represents a relationship between a Booking and a TimeSlot.
/// One booking can span multiple consecutive hourly TimeSlots.
/// Includes CourtId and BookingDate to enable database-level double-booking protection.
/// </summary>
public class BookingTimeSlot
{
    public int Id { get; set; }

    public int BookingId { get; set; }

    /// <summary>
    /// The court associated with this booking timeslot.
    /// Denormalized from Booking for double-booking constraint.
    /// </summary>
    public int CourtId { get; set; }

    /// <summary>
    /// The date of the booking.
    /// Denormalized from Booking for double-booking constraint.
    /// </summary>
    public DateOnly BookingDate { get; set; }

    public int TimeSlotId { get; set; }

    /// <summary>
    /// Order of this slot within the booking (0-based).
    /// Used to ensure slots are consecutive and for validation.
    /// </summary>
    public int SlotOrder { get; set; }

    /// <summary>
    /// Indicates if this booking timeslot is active.
    /// Cancelled bookings can remain in history with IsActive = false.
    /// Database constraint ensures no duplicates where IsActive = true.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Booking Booking { get; set; } = null!;

    public TimeSlot TimeSlot { get; set; } = null!;
}
