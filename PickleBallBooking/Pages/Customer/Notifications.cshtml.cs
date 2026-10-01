using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Customer;

/// <summary>
/// Phase 36: Player in-app notification feed.
/// Displays all notifications for the current player, with mark-as-read functionality.
/// </summary>
public class NotificationsModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly AppNotificationService _notifications;

    public NotificationsModel(UserManager<IdentityUser> userManager, AppNotificationService notifications)
    {
        _userManager   = userManager;
        _notifications = notifications;
    }

    public IReadOnlyList<AppNotification> Notifications { get; private set; } = [];
    public int UnreadCount { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null) return RedirectToPage("/Account/Login");

        Notifications = await _notifications.GetForUserAsync(userId, limit: 100);
        UnreadCount   = Notifications.Count(n => !n.IsRead);
        return Page();
    }

    public async Task<IActionResult> OnPostMarkReadAsync(int id)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null) return RedirectToPage("/Account/Login");

        await _notifications.MarkReadAsync(id, userId);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostMarkAllReadAsync()
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null) return RedirectToPage("/Account/Login");

        await _notifications.MarkAllReadAsync(userId);
        StatusMessage = "All notifications marked as read.";
        return RedirectToPage();
    }
}
