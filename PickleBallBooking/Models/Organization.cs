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

    /// <summary>
    /// Telegram Chat ID or Group ID for real-time staff alerts.
    /// Nullable — Telegram notifications are skipped when not set.
    /// Configured by the org admin in OrgSettings.
    /// </summary>
    [MaxLength(100)]
    public string? TelegramChatId { get; set; }

    /// <summary>Relative storage path to the organization's custom brand logo in Supabase public storage.</summary>
    [MaxLength(500)]
    public string? LogoPath { get; set; }

    /// <summary>Relative storage path to the organization's custom hero image in Supabase public storage.
    /// Displayed on the public landing page instead of the default hero artwork when set.
    /// Configurable by the org admin and platform admin.
    /// </summary>
    [MaxLength(500)]
    public string? HeroImagePath { get; set; }

    // ── Tenant Branding (custom homepage) ─────────────────────────────────

    /// <summary>Short tagline displayed in the hero section, e.g. "Manila's Premier Pickleball Venue".</summary>
    [MaxLength(200)]
    public string? Tagline { get; set; }

    /// <summary>Short about/welcome paragraph shown on the homepage below the hero.</summary>
    [MaxLength(1000)]
    public string? AboutText { get; set; }

    /// <summary>
    /// Primary brand color in 6-digit hex (e.g. "#16a34a").
    /// When set, injected as --pb-primary CSS variable to override the default green.
    /// </summary>
    [MaxLength(7)]
    public string? PrimaryColorHex { get; set; }

    /// <summary>Facebook page URL for this venue (shown in footer / homepage).</summary>
    [MaxLength(300)]
    public string? FacebookUrl { get; set; }

    /// <summary>Instagram profile URL for this venue.</summary>
    [MaxLength(300)]
    public string? InstagramUrl { get; set; }

    /// <summary>X / Twitter profile URL for this venue.</summary>
    [MaxLength(300)]
    public string? TwitterUrl { get; set; }

    /// <summary>
    /// When true, upcoming activities are shown as a section on the public homepage.
    /// Defaults to false so existing tenants without activities are unaffected.
    /// </summary>
    public bool ShowActivitiesOnHome { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
