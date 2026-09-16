using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickleBallBooking.Models;

public class Booking
{
    public int Id { get; set; }

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

    public int? TimeSlotId { get; set; }

    [ForeignKey(nameof(TimeSlotId))]
    public TimeSlot? TimeSlot { get; set; }

    public BookingStatus BookingStatus { get; set; } = BookingStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
