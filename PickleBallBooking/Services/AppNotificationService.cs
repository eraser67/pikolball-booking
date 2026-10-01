using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

/// <summary>
/// Phase 36: In-app notification service for player/customer accounts.
///
/// Creates, marks read, and queries AppNotification records. All operations are
/// user-scoped (platform-level — no tenant isolation required, since players are
/// platform accounts).
///
/// Notification delivery is always fire-and-forget safe: any exception is caught and
/// logged so a notification failure never disrupts the caller's primary operation.
/// </summary>
public sealed class AppNotificationService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AppNotificationService> _logger;

    public AppNotificationService(ApplicationDbContext context, ILogger<AppNotificationService> logger)
    {
        _context = context;
        _logger  = logger;
    }

    // ── Creation ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a notification for a single user.
    /// Safe to call fire-and-forget: exceptions are caught and logged.
    /// </summary>
    public async Task CreateAsync(
        string userId,
        AppNotificationType type,
        string title,
        string? body = null,
        string? actionUrl = null)
    {
        try
        {
            _context.AppNotifications.Add(new AppNotification
            {
                UserId    = userId,
                Type      = type,
                Title     = title,
                Body      = body,
                ActionUrl = actionUrl,
                IsRead    = false,
                CreatedAt = DateTime.UtcNow,
            });
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create in-app notification for user {UserId} ({Type})", userId, type);
        }
    }

    /// <summary>
    /// Creates notifications for multiple users (bulk — one per userId).
    /// Safe to call fire-and-forget.
    /// </summary>
    public async Task CreateBulkAsync(
        IEnumerable<string> userIds,
        AppNotificationType type,
        string title,
        string? body = null,
        string? actionUrl = null)
    {
        try
        {
            var now = DateTime.UtcNow;
            foreach (var userId in userIds)
            {
                _context.AppNotifications.Add(new AppNotification
                {
                    UserId    = userId,
                    Type      = type,
                    Title     = title,
                    Body      = body,
                    ActionUrl = actionUrl,
                    IsRead    = false,
                    CreatedAt = now,
                });
            }
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create bulk in-app notifications ({Type})", type);
        }
    }

    // ── Read / Count ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the count of unread notifications for the given user.
    /// Returns 0 on error.
    /// </summary>
    public async Task<int> GetUnreadCountAsync(string userId)
    {
        try
        {
            return await _context.AppNotifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get unread notification count for user {UserId}", userId);
            return 0;
        }
    }

    /// <summary>
    /// Returns the most recent notifications for the given user (default: latest 50),
    /// ordered newest-first.
    /// </summary>
    public async Task<List<AppNotification>> GetForUserAsync(string userId, int limit = 50)
    {
        try
        {
            return await _context.AppNotifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load notifications for user {UserId}", userId);
            return [];
        }
    }

    // ── Mark Read ────────────────────────────────────────────────────────────

    /// <summary>Marks a single notification as read. Returns false if not found or not owned by user.</summary>
    public async Task<bool> MarkReadAsync(int notificationId, string userId)
    {
        try
        {
            var n = await _context.AppNotifications
                .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);

            if (n is null) return false;

            n.IsRead  = true;
            n.ReadAt  = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark notification {Id} as read for user {UserId}", notificationId, userId);
            return false;
        }
    }

    /// <summary>Marks all unread notifications for the user as read in a single UPDATE.</summary>
    public async Task MarkAllReadAsync(string userId)
    {
        try
        {
            var now = DateTime.UtcNow;
            var unread = await _context.AppNotifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ToListAsync();

            foreach (var n in unread)
            {
                n.IsRead = true;
                n.ReadAt = now;
            }

            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark all notifications as read for user {UserId}", userId);
        }
    }

    // ── Icon / Color helpers (for view rendering) ────────────────────────────

    /// <summary>Returns a Bootstrap Icons class appropriate for the notification type.</summary>
    public static string GetIcon(AppNotificationType type) => type switch
    {
        AppNotificationType.ActivityRsvpConfirmed  => "bi-check-circle-fill text-success",
        AppNotificationType.ActivityWaitlisted     => "bi-hourglass-split text-warning",
        AppNotificationType.ActivityWaitlistPromoted => "bi-star-fill text-success",
        AppNotificationType.ActivityRsvpCancelled  => "bi-x-circle-fill text-secondary",
        AppNotificationType.ActivityCancelled      => "bi-exclamation-triangle-fill text-danger",
        AppNotificationType.ActivityReminder       => "bi-bell-fill text-info",
        AppNotificationType.ActivityNewRegistration => "bi-person-plus-fill text-primary",
        AppNotificationType.ActivityRsvpCancelledAdmin => "bi-person-dash-fill text-warning",
        _                                          => "bi-bell text-muted",
    };
}
