using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickleBallBooking.Models;

/// <summary>
/// Phase 25: a payment record tied to a booking.
///
/// IMPORTANT — tenant isolation:
/// - OrganizationId is stamped automatically by the write guard in ApplicationDbContext.
/// - The global query filter ensures a tenant admin can only see their own tenant's payments.
/// - Customers submit via BookingReference lookup — no auth required, but they CANNOT
///   transition the status to Verified. That transition is admin-only.
/// </summary>
public class Payment
{
    public int Id { get; set; }

    /// <summary>
    /// Stamped automatically by the DbContext write guard.
    /// Never accepted from client input.
    /// </summary>
    public int OrganizationId { get; set; }

    public int BookingId { get; set; }

    [ForeignKey(nameof(BookingId))]
    public Booking? Booking { get; set; }

    /// <summary>Copied from Booking.Price at the time the payment is created.</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal Amount { get; set; }

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.GCash;

    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;

    /// <summary>GCash reference number entered by the customer.</summary>
    [MaxLength(100)]
    public string? ReferenceNumber { get; set; }

    /// <summary>Supabase Storage path for the optional proof screenshot.</summary>
    [MaxLength(500)]
    public string? ProofImagePath { get; set; }

    /// <summary>Timestamp when the customer submitted a reference number.</summary>
    public DateTime? SubmittedAt { get; set; }

    /// <summary>Timestamp when an admin verified or rejected the payment.</summary>
    public DateTime? VerifiedAt { get; set; }

    /// <summary>
    /// Identity UserId of the admin who verified or rejected.
    /// Null until the payment is acted on by an admin.
    /// </summary>
    [MaxLength(450)]
    public string? VerifiedByUserId { get; set; }

    /// <summary>Optional notes written by the admin (especially for rejections).</summary>
    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
