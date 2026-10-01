using System.ComponentModel.DataAnnotations;

namespace PickleBallBooking.Models;

/// <summary>
/// Phase 36: In-app notification record for a player.
///
/// Platform-level (no OrganizationId / no tenant query filter) — players are platform
/// accounts that may be registered at multiple venues. Notifications belong to the user,
/// not to any single tenant.
///
/// Notifications are created by <see cref="PickleBallBooking.Services.AppNotificationService"/>
/// and consumed by the player's notification feed (<c>/Customer/Notifications</c>) and the
/// unread-count badge in the shared layout.
/// </summary>
public class AppNotification
{
    public int Id { get; set; }

    /// <summary>FK to AspNetUsers.Id — the player who owns this notification.</summary>
    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Categorised type so UI can render appropriate icons/colours.</summary>
    public AppNotificationType Type { get; set; }

    /// <summary>Short notification title (e.g. "Spot Confirmed — Open Play").</summary>
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Longer supporting body text (optional, 1–2 sentences).</summary>
    [MaxLength(500)]
    public string? Body { get; set; }

    /// <summary>
    /// Optional deep-link relative URL (e.g. "/Activities/42") that the player can
    /// click to navigate to the relevant page. Null = no link.
    /// </summary>
    [MaxLength(300)]
    public string? ActionUrl { get; set; }

    /// <summary>True once the player has viewed / marked-read this notification.</summary>
    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Populated when the notification is marked as read.</summary>
    public DateTime? ReadAt { get; set; }
}
