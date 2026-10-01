using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickleBallBooking.Models;

public class Booking
{
    public int Id { get; set; }

    public int OrganizationId { get; set; }

    [Required]
    [MaxLength(20)]
    public string BookingReference { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string CustomerName { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string CustomerPhone { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string CustomerEmail { get; set; } = string.Empty;

    public int CourtId { get; set; }

    [ForeignKey(nameof(CourtId))]
    public Court? Court { get; set; }

    public DateOnly BookingDate { get; set; }

    [Column(TypeName = "time without time zone")]
    public TimeSpan StartTime { get; set; }

    [Column(TypeName = "time without time zone")]
    public TimeSpan EndTime { get; set; }

    [Column(TypeName = "numeric(6,2)")]
    public decimal? DurationHours { get; set; }

        [Column(TypeName = "decimal(10,2)")]
    public decimal Price { get; set; }

    public BookingStatus BookingStatus { get; set; } = BookingStatus.Pending;

    /// <summary>Phase 38: Timestamp when the booking party checked in at the venue.</summary>
    public DateTime? CheckedInAt { get; set; }

    /// <summary>Phase 38: User ID of the staff member who recorded the check-in.</summary>
    [MaxLength(450)]
    public string? CheckedInByUserId { get; set; }

    /// <summary>Phase 38: Method used for check-in.</summary>
    public CheckInMethod? CheckInMethod { get; set; }

    /// <summary>Phase 38: True if the booking party was marked as a no-show.</summary>
    public bool IsNoShow { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Collection of time slots associated with this booking.
    /// One booking can span multiple consecutive hourly slots.
    /// </summary>
    public ICollection<BookingTimeSlot> TimeSlots { get; set; } = new List<BookingTimeSlot>();
}
