using System.ComponentModel.DataAnnotations;

namespace PickleBallBooking.Models;

/// <summary>
/// A single tenant-configured payment option (e.g. "GCash", "Maya QR", "BDO Bank QR").
///
/// Tenants can define as many active options as they want.
/// Each option has its own QR code image, account name, account number, and instructions.
/// Customers see a dropdown/selector on the booking form; the matching QR is shown on the payment page.
///
/// Tenant-owned: OrganizationId is stamped by the DbContext write guard.
/// </summary>
public class TenantPaymentOption
{
    public int Id { get; set; }

    /// <summary>Stamped by the DbContext write guard. Never accepted from client input.</summary>
    public int OrganizationId { get; set; }

    /// <summary>Display label shown to customers, e.g. "GCash", "Maya", "BDO Bank QR".</summary>
    [Required]
    [MaxLength(100)]
    public string Label { get; set; } = string.Empty;

    /// <summary>Account holder name shown to customers (e.g. "Juan Dela Cruz").</summary>
    [Required]
    [MaxLength(100)]
    public string AccountName { get; set; } = string.Empty;

    /// <summary>Account number or mobile number (optional — shown when QR is not available).</summary>
    [MaxLength(100)]
    public string? AccountNumber { get; set; }

    /// <summary>
    /// Supabase Storage path for the QR code image (court-images bucket, public).
    /// Resolved to a public URL at runtime.
    /// Path: organizations/{orgId}/payment-options/{id}/qr.{ext}
    /// </summary>
    [MaxLength(500)]
    public string? QRCodeImagePath { get; set; }

    /// <summary>Optional instructions shown to the customer after selecting this method.</summary>
    [MaxLength(1000)]
    public string? Instructions { get; set; }

    /// <summary>Sort order (lower = shown first).</summary>
    public int DisplayOrder { get; set; }

    /// <summary>When false, this option is hidden from the booking form and payment page.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
