using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Activities;

/// <summary>Phase 35: Edit an existing ActivitySeries and optionally propagate changes to future occurrences.</summary>
public class SeriesEditModel : PageModel
{
    private readonly ActivitySeriesService _seriesService;
    private readonly ApplicationDbContext _context;

    public SeriesEditModel(ActivitySeriesService seriesService, ApplicationDbContext context)
    {
        _seriesService = seriesService;
        _context = context;
    }

    [BindProperty] public int Id { get; set; }
    [BindProperty] public SeriesCreateModel.SeriesInput Input { get; set; } = new();
    [BindProperty] public bool ApplyToFutureOccurrences { get; set; }
    public SelectList? CourtOptions { get; private set; }
    public ActivitySeries? Series { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Series = await _seriesService.GetByIdAsync(id);
        if (Series is null) return NotFound();
        Id = id;

        Input.Name                    = Series.Name;
        Input.Description             = Series.Description;
        Input.Format                  = Series.Format;
        Input.SkillLevel              = Series.SkillLevel;
        Input.RecurrenceType          = Series.RecurrenceType;
        Input.WeeklyDays              = Series.WeeklyDays;
        Input.MonthlyDayOfMonth       = Series.MonthlyDayOfMonth;
        Input.SpecificDates           = Series.SpecificDates;
        Input.SeriesStartDateStr      = Series.SeriesStartDate.ToString("yyyy-MM-dd");
        Input.SeriesEndDateStr        = Series.SeriesEndDate?.ToString("yyyy-MM-dd");
        Input.OccurrenceStartTimeStr  = Series.OccurrenceStartTime.ToString(@"hh\:mm");
        Input.OccurrenceEndTimeStr    = Series.OccurrenceEndTime.ToString(@"hh\:mm");
        Input.MaxCapacity             = Series.MaxCapacity;
        Input.PricePerPlayer          = Series.PricePerPlayer;
        Input.SelectedCourtIds        = await _seriesService.GetCurrentCourtIdsAsync(id);

        await LoadCourtsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadCourtsAsync();
        if (!ModelState.IsValid) return Page();

        if (!DateOnly.TryParse(Input.SeriesStartDateStr, out var startDate))
        {
            ModelState.AddModelError("Input.SeriesStartDateStr", "Invalid start date.");
            return Page();
        }

        DateOnly? endDate = null;
        if (!string.IsNullOrWhiteSpace(Input.SeriesEndDateStr))
        {
            if (!DateOnly.TryParse(Input.SeriesEndDateStr, out var parsedEnd))
            {
                ModelState.AddModelError("Input.SeriesEndDateStr", "Invalid end date.");
                return Page();
            }
            endDate = parsedEnd;
            if (endDate.Value <= startDate)
            {
                ModelState.AddModelError("Input.SeriesEndDateStr", "End date must be after start date.");
                return Page();
            }
        }

        if (!TimeSpan.TryParse(Input.OccurrenceStartTimeStr, out var startTime) ||
            !TimeSpan.TryParse(Input.OccurrenceEndTimeStr, out var endTime))
        {
            ModelState.AddModelError("", "Invalid time values.");
            return Page();
        }

        if (endTime <= startTime)
        {
            ModelState.AddModelError("Input.OccurrenceEndTimeStr", "End time must be after start time.");
            return Page();
        }

        var courtIds = Input.SelectedCourtIds ?? [];
        var result = await _seriesService.UpdateAsync(
            id:                       Id,
            name:                     Input.Name,
            description:              Input.Description,
            format:                   Input.Format,
            skillLevel:               Input.SkillLevel,
            recurrenceType:           Input.RecurrenceType,
            weeklyDays:               Input.WeeklyDays,
            monthlyDayOfMonth:        Input.MonthlyDayOfMonth,
            specificDates:            Input.SpecificDates,
            seriesStartDate:          startDate,
            seriesEndDate:            endDate,
            occurrenceStartTime:      startTime,
            occurrenceEndTime:        endTime,
            maxCapacity:              Input.MaxCapacity,
            pricePerPlayer:           Input.PricePerPlayer,
            courtIds:                 courtIds,
            applyToFutureOccurrences: ApplyToFutureOccurrences);

        if (result is null) return NotFound();

        TempData["StatusMessage"] = ApplyToFutureOccurrences
            ? $"Series \"{Input.Name}\" updated and changes applied to future occurrences."
            : $"Series \"{Input.Name}\" updated.";

        return RedirectToPage("SeriesDetail", new { id = Id });
    }

    private async Task LoadCourtsAsync()
    {
        var courts = await _context.Courts
            .Where(c => c.Status == CourtStatus.Active)
            .OrderBy(c => c.Name)
            .ToListAsync();
        CourtOptions = new SelectList(courts, "Id", "Name");
    }
}
