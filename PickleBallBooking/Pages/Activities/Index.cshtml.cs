using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Activities;

/// <summary>
/// Phase 34: player-facing activity browse and RSVP page.
/// Allows authenticated customers to join activities (Confirmed or Waitlisted)
/// and cancel existing RSVPs with automatic promotion.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ActivityService _activityService;
    private readonly ActivityRsvpService _rsvpService;
    private readonly UserManager<IdentityUser> _userManager;

    public IndexModel(
        ActivityService activityService,
        ActivityRsvpService rsvpService,
        UserManager<IdentityUser> userManager)
    {
        _activityService = activityService;
        _rsvpService = rsvpService;
        _userManager = userManager;
    }

    public List<Activity> Activities { get; private set; } = [];
    public Dictionary<int, ActivityRsvp> UserRsvps { get; private set; } = [];
    public Dictionary<int, (int Confirmed, int Waitlist)> RsvpCounts { get; private set; } = [];
    public HashSet<int> AdminActivityIds { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Activities = await _activityService.GetPublishedAsync();

        var activityIds = Activities.Select(a => a.Id).ToList();
        RsvpCounts = await _rsvpService.GetRsvpCountsForActivitiesAsync(activityIds);

        var userId = _userManager.GetUserId(User);
        if (!string.IsNullOrEmpty(userId))
        {
            UserRsvps = await _rsvpService.GetUserRsvpsForActivitiesAsync(activityIds, userId);
            AdminActivityIds = await _rsvpService.GetAdminActivityIdsAsync(activityIds, userId);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostJoinAsync(int activityId, string? notes)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToPage("/Account/Login", new { returnUrl = "/Activities" });
        }

        var result = await _rsvpService.JoinActivityAsync(activityId, user.Id, notes);
        if (result.Success)
        {
            StatusMessage = result.Message;
        }
        else
        {
            ErrorMessage = result.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCancelAsync(int activityId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToPage("/Account/Login", new { returnUrl = "/Activities" });
        }

        var result = await _rsvpService.CancelRsvpAsync(activityId, user.Id);
        if (result.Success)
        {
            StatusMessage = result.Message;
        }
        else
        {
            ErrorMessage = result.Message;
        }

        return RedirectToPage();
    }
}
