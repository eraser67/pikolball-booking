using System.ComponentModel.DataAnnotations;

namespace PickleBallBooking.Models;

/// <summary>
/// Single-row table that stores platform-level configuration.
/// Only one row (Id = 1) will ever exist; accessed via PlatformSettingsService.
/// </summary>
public class PlatformSettings
{
    public int Id { get; set; } = 1;

    /// <summary>
    /// When true, the public "Register Your Venue" page is accessible and the
    /// call-to-action button appears on the platform homepage.
    /// Platform owner can toggle this off to close registrations at any time.
    /// </summary>
    public bool AllowTenantRegistration { get; set; } = false;

    /// <summary>
    /// Optional message shown on the registration form (e.g. instructions,
    /// welcome text, terms reference). Supports plain text.
    /// </summary>
    [MaxLength(600)]
    public string? RegistrationMessage { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
