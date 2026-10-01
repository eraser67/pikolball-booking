using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Activities;

public class LeaderboardModel : PageModel
{
    private readonly IStandingsService _standingsService;

    public LeaderboardModel(IStandingsService standingsService)
    {
        _standingsService = standingsService;
    }

    public TenantLeaderboardDto Leaderboard { get; private set; } = null!;

    [BindProperty(SupportsGet = true)]
    public RoundRobinFormat? Format { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Timeframe { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    public int MinMatches { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public async Task OnGetAsync()
    {
        var filter = new LeaderboardFilterDto(
            Format: Format,
            MinMatchesPlayed: MinMatches,
            Timeframe: Timeframe,
            SearchQuery: Search
        );

        Leaderboard = await _standingsService.GetTenantLeaderboardAsync(filter);
    }
}
