using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Activities;

[Authorize(Policy = "TenantAdmin")]
public class RoundRobinModel : PageModel
{
    private readonly IRoundRobinService _roundRobinService;

    public RoundRobinModel(IRoundRobinService roundRobinService)
    {
        _roundRobinService = roundRobinService;
    }

    public RoundRobinEventOverviewDto Overview { get; private set; } = null!;

    [BindProperty]
    public RoundRobinConfigDto Config { get; set; } = new(
        RoundRobinFormat.RotatingPartners,
        3,
        15,
        5,
        ScoringType.RallyScoring,
        11,
        true,
        null
    );

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var overview = await _roundRobinService.GetEventOverviewAsync(id);
        if (overview == null) return NotFound();

        Overview = overview;

        // Prepopulate config if event already exists
        if (overview.EventId > 0)
        {
            Config = new RoundRobinConfigDto(
                overview.Format,
                overview.NumberOfRounds,
                overview.MatchDurationMinutes,
                overview.BreakDurationMinutes,
                overview.ScoringType,
                overview.PointsToWin,
                overview.WinByTwo,
                overview.StartTime
            );
        }
        else
        {
            var defaultRounds = Math.Max(1, Math.Min(5, overview.Participants.Count > 1 ? overview.Participants.Count - 1 : 3));
            var totalMins = (int)(overview.EndTime - overview.StartTime).TotalMinutes;
            if (totalMins <= 0) totalMins = 120; // 2 hours fallback

            var defaultBreak = 5;
            var totalBreakMins = Math.Max(0, (defaultRounds - 1) * defaultBreak);
            var computedDuration = Math.Max(15, Math.Min(60, (totalMins - totalBreakMins) / defaultRounds));

            Config = new RoundRobinConfigDto(
                RoundRobinFormat.RotatingPartners,
                defaultRounds,
                computedDuration,
                defaultBreak,
                ScoringType.RallyScoring,
                11,
                true,
                overview.StartTime
            );
        }

        return Page();
    }

    public async Task<IActionResult> OnPostGenerateAsync(int id)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var result = await _roundRobinService.GenerateScheduleAsync(id, Config, adminUserId);

        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostClearAsync(int id)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var result = await _roundRobinService.ClearScheduleAsync(id, adminUserId);

        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostToggleLockAsync(int id, bool lockSchedule)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var result = await _roundRobinService.ToggleLockAsync(id, lockSchedule, adminUserId);

        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSwapPlayersAsync(int id, int matchId, string slotA, string slotB)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var result = await _roundRobinService.SwapMatchPlayersAsync(matchId, slotA, slotB, adminUserId);

        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostChangeCourtAsync(int id, int matchId, int newCourtId)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var result = await _roundRobinService.UpdateMatchCourtAsync(matchId, newCourtId, adminUserId);

        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }
}
