using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Activities;

public class IndexModel : PageModel
{
    private readonly ActivityService _activityService;
    public IndexModel(ActivityService activityService) => _activityService = activityService;

    public List<Activity> Activities { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Activities = await _activityService.GetAllAsync();
    }
}
