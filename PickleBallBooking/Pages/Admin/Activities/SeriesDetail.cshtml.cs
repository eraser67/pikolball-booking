using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Activities;

/// <summary>Phase 35: Detail view and lifecycle actions for a recurring ActivitySeries.</summary>
public class SeriesDetailModel : PageModel
{
    private readonly ActivitySeriesService _seriesService;

    public SeriesDetailModel(ActivitySeriesService seriesService)
        => _seriesService = seriesService;

    public ActivitySeries? Series { get; private set; }

    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Series = await _seriesService.GetByIdAsync(id);
        if (Series is null) return NotFound();
        return Page();
    }

    // ── Series lifecycle actions ──────────────────────────────────────────

    public async Task<IActionResult> OnPostPauseAsync(int id)
    {
        var ok = await _seriesService.PauseAsync(id);
        StatusMessage = ok ? "Series paused. New occurrences will not be generated while paused."
                           : "Series not found.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostResumeAsync(int id)
    {
        Series = await _seriesService.GetByIdAsync(id);
        if (Series is null) return NotFound();

        var courtIds = await _seriesService.GetCurrentCourtIdsAsync(id);
        var (success, generated) = await _seriesService.ResumeAsync(id, courtIds);
        StatusMessage = success
            ? $"Series resumed. {generated} new occurrence(s) generated."
            : "Series could not be resumed (may not be paused).";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCancelSeriesAsync(int id)
    {
        var (success, cancelled) = await _seriesService.CancelSeriesAsync(id);
        StatusMessage = success
            ? $"Series cancelled. {cancelled} future occurrence(s) were also cancelled."
            : "Series not found.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCancelFromDateAsync(int id, string fromDateStr)
    {
        if (!DateOnly.TryParse(fromDateStr, out var fromDate))
        {
            ErrorMessage = "Invalid date.";
            return RedirectToPage(new { id });
        }

        var (success, cancelled) = await _seriesService.CancelSeriesFromDateAsync(id, fromDate);
        StatusMessage = success
            ? $"Series and {cancelled} occurrence(s) from {fromDate:MMM d, yyyy} onward were cancelled."
            : "Series not found.";
        return RedirectToPage(new { id });
    }

    // ── Single occurrence action ──────────────────────────────────────────

    public async Task<IActionResult> OnPostCancelOccurrenceAsync(int id, int activityId)
    {
        var ok = await _seriesService.CancelOccurrenceAsync(activityId);
        if (ok) StatusMessage = "Occurrence cancelled.";
        else ErrorMessage = "Could not cancel this occurrence.";
        return RedirectToPage(new { id });
    }
}
