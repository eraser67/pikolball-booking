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
    private readonly IMatchScoringService _scoringService;
    private readonly IStandingsService _standingsService;
    private readonly ActivityService _activityService;

    public RoundRobinModel(
        IRoundRobinService roundRobinService,
        IMatchScoringService scoringService,
        IStandingsService standingsService,
        ActivityService activityService)
    {
        _roundRobinService = roundRobinService;
        _scoringService = scoringService;
        _standingsService = standingsService;
        _activityService = activityService;
    }

    public RoundRobinEventOverviewDto Overview { get; private set; } = null!;
    public EventStandingsDto? Standings { get; private set; }

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
        if (overview.EventId > 0)
        {
            Standings = await _standingsService.GetEventStandingsAsync(id);
        }

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

    public async Task<IActionResult> OnPostUpdateRoundTimeAsync(int id, int roundNumber, TimeSpan startTime, TimeSpan endTime)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var result = await _roundRobinService.UpdateRoundTimeAsync(id, roundNumber, startTime, endTime, adminUserId);

        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostStartMatchAsync(int id, int matchId)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var result = await _scoringService.StartMatchAsync(matchId, adminUserId);

        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRecordScoreAsync(int id, int matchId, int team1Score, int team2Score, bool isLiveUpdate, string? notes)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var dto = new SubmitScoreDto(team1Score, team2Score, null, isLiveUpdate, notes);
        var result = await _scoringService.RecordScoreAsync(matchId, dto, adminUserId, isPlayerSubmission: false);

        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostFinalizeMatchAsync(int id, int matchId)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var result = await _scoringService.FinalizeMatchAsync(matchId, adminUserId);

        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCorrectScoreAsync(int id, int matchId, int team1Score, int team2Score, string reason)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var dto = new CorrectScoreDto(team1Score, team2Score, reason);
        var result = await _scoringService.CorrectFinalizedScoreAsync(matchId, dto, adminUserId);

        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCompleteActivityAsync(int id)
    {
        await _activityService.SetStatusAsync(id, ActivityStatus.Completed);
        StatusMessage = "Activity has been officially marked as Completed! All matches have concluded and official standings are finalized.";
        return RedirectToPage(new { id });
    }
}
