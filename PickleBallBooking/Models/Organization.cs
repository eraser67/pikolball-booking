using System.ComponentModel.DataAnnotations;

namespace PickleBallBooking.Models;

public class Organization
{
    public int Id { get; set; }

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Slug { get; set; } = string.Empty;

    public OrganizationStatus Status { get; set; } = OrganizationStatus.Active;

    /// <summary>Street address shown on the public homepage.</summary>
    [MaxLength(300)]
    public string? Address { get; set; }

    /// <summary>Google Maps latitude for the embedded map.</summary>
    public double? Latitude { get; set; }

    /// <summary>Google Maps longitude for the embedded map.</summary>
    public double? Longitude { get; set; }

    /// <summary>
    /// Email address for organization admin notifications (new bookings, payment submissions, cancellations).
    /// Nullable — notifications are skipped when not set.
    /// Configured by the org admin in OrgSettings.
    /// </summary>
    [MaxLength(256)]
    public string? NotificationEmail { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
