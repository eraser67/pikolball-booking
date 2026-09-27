using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Activities;

/// <summary>
/// Phase 33: player-facing activity browse page.
/// Shows Published, RegistrationOpen, Full and RegistrationClosed activities for the current tenant.
/// RSVP (joining) will be implemented in Phase 34.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ActivityService _activityService;
    public IndexModel(ActivityService activityService) => _activityService = activityService;

    public List<Activity> Activities { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        Activities = await _activityService.GetPublishedAsync();
        return Page();
    }
}
