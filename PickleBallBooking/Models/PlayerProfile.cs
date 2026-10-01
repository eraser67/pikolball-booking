using System.ComponentModel.DataAnnotations;

namespace PickleBallBooking.Models;

/// <summary>
/// Phase 32: platform-level player profile record.
///
/// This is a GLOBAL entity — it has no OrganizationId and is NOT subject
/// to the EF Core tenant query filter. The player identity belongs to the
/// platform, not to any single tenant.
///
/// Tenant-specific participation data (activity history, match results,
/// standings) will carry OrganizationId and live in separate tables (Phase 33+).
///
/// Linked to the ASP.NET Core Identity user via UserId (the IdentityUser.Id string).
/// One player may have at most one PlayerProfile (one-to-one with IdentityUser).
/// </summary>
public class PlayerProfile
{
    public int Id { get; set; }

    /// <summary>Foreign key to AspNetUsers.Id.</summary>
    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    // ── Avatar ───────────────────────────────────────────────────────────
    /// <summary>
    /// Supabase Storage path for the player's avatar image.
    /// Format: players/{userId}/avatar/avatar.{ext}
    /// Null when no avatar has been uploaded.
    /// The public URL is derived at display time via ICourtImageStorage.GetPublicUrl.
    /// </summary>
    [MaxLength(500)]
    public string? AvatarPath { get; set; }

    // ── Name ─────────────────────────────────────────────────────────────
    [Required]
    [MaxLength(50)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Publicly visible name shown in activity rosters, leaderboards, etc.
    /// Defaults to FirstName + LastName but can be customized.
    /// </summary>
    [MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    // ── Contact ──────────────────────────────────────────────────────────
    /// <summary>Mobile number — private by default (PrivacyMobile controls visibility).</summary>
    [MaxLength(20)]
    public string? Mobile { get; set; }

    // ── Pickleball Info ──────────────────────────────────────────────────
    public PlayerSkillLevel SkillLevel { get; set; } = PlayerSkillLevel.Beginner;

    public PlayingHand PlayingHand { get; set; } = PlayingHand.Right;

    // ── Bio & Location ───────────────────────────────────────────────────
    [MaxLength(500)]
    public string? Bio { get; set; }

    /// <summary>City / region — not a precise address.</summary>
    [MaxLength(100)]
    public string? Location { get; set; }

    // ── Privacy Settings ─────────────────────────────────────────────────
    /// <summary>When true, this profile appears in platform-wide player search (Phase 50+).</summary>
    public bool IsDiscoverable { get; set; } = true;

    /// <summary>When true, mobile number is visible to other players.</summary>
    public bool PrivacyMobile { get; set; } = false;

    /// <summary>Phase 43: Controls who can view this player's match history and statistics.</summary>
    public MatchHistoryPrivacyLevel PrivacyMatchHistory { get; set; } = MatchHistoryPrivacyLevel.Public;

    // ── Timestamps ───────────────────────────────────────────────────────
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
