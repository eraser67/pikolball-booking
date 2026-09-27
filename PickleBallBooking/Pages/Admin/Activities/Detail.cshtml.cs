using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Activities;

public class DetailModel : PageModel
{
    private readonly ActivityService _activityService;
    public DetailModel(ActivityService activityService) => _activityService = activityService;

    public Activity? Activity { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Activity = await _activityService.GetByIdAsync(id);
        return Activity is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostSetStatusAsync(int id, ActivityStatus status)
    {
        await _activityService.SetStatusAsync(id, status);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await _activityService.DeleteAsync(id);
        return RedirectToPage("Index");
    }
}
