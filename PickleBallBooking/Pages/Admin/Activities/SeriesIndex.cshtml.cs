using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Activities;

/// <summary>Phase 35: Lists all recurring activity series for the current tenant.</summary>
public class SeriesIndexModel : PageModel
{
    private readonly ActivitySeriesService _seriesService;

    public SeriesIndexModel(ActivitySeriesService seriesService)
        => _seriesService = seriesService;

    public List<ActivitySeries> Series { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Series = await _seriesService.GetAllAsync();
    }
}
