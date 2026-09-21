using System.ComponentModel.DataAnnotations;

namespace PickleBallBooking.Models;

/// <summary>
/// Phase 25: GCash payment configuration for an organization.
///
/// One record per organization (enforced by unique index on OrganizationId).
/// Tenant-owned — write guard stamps OrganizationId automatically.
/// </summary>
public class OrganizationPaymentSettings
{
    public int Id { get; set; }

    /// <summary>Stamped by the write guard. One record per organization.</summary>
    public int OrganizationId { get; set; }

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.GCash;

    /// <summary>GCash account holder name shown to customers.</summary>
    [Required]
    [MaxLength(100)]
    public string AccountName { get; set; } = string.Empty;

    /// <summary>GCash mobile number shown to customers (optional if QR code is provided).</summary>
    [MaxLength(50)]
    public string? AccountNumber { get; set; } = string.Empty;

    /// <summary>
    /// Supabase Storage path for the GCash QR code image.
    /// Stored in the court-images bucket under organizations/{orgId}/qr/gcash.{ext}.
    /// Public URL is resolved at runtime.
    /// </summary>
    [MaxLength(500)]
    public string? QRCodeImagePath { get; set; }

    /// <summary>Custom instructions shown to the customer on the payment page.</summary>
    [MaxLength(1000)]
    public string? Instructions { get; set; }

    /// <summary>When false, the payment prompt is hidden on the booking confirmation page.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
