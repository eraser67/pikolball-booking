using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Activities;

/// <summary>Phase 35: Create a new recurring ActivitySeries.</summary>
public class SeriesCreateModel : PageModel
{
    private readonly ActivitySeriesService _seriesService;
    private readonly ApplicationDbContext _context;

    public SeriesCreateModel(ActivitySeriesService seriesService, ApplicationDbContext context)
    {
        _seriesService = seriesService;
        _context = context;
    }

    [BindProperty] public SeriesInput Input { get; set; } = new();
    public SelectList? CourtOptions { get; private set; }

    public async Task OnGetAsync()
    {
        var today = AppClock.TodayLocal;
        Input.SeriesStartDateStr = today.AddDays(1).ToString("yyyy-MM-dd");
        Input.OccurrenceStartTimeStr = "08:00";
        Input.OccurrenceEndTimeStr   = "10:00";
        await LoadCourtsAsync();
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

        // Validate recurrence settings
        if (Input.RecurrenceType == RecurrenceType.Weekly && string.IsNullOrWhiteSpace(Input.WeeklyDays))
        {
            ModelState.AddModelError("Input.WeeklyDays", "Select at least one day of the week.");
            return Page();
        }

        if (Input.RecurrenceType == RecurrenceType.Monthly &&
            (!Input.MonthlyDayOfMonth.HasValue || Input.MonthlyDayOfMonth < 1 || Input.MonthlyDayOfMonth > 31))
        {
            ModelState.AddModelError("Input.MonthlyDayOfMonth", "Select a valid day of month (1–31).");
            return Page();
        }

        if (Input.RecurrenceType == RecurrenceType.SpecificDays && string.IsNullOrWhiteSpace(Input.SpecificDates))
        {
            ModelState.AddModelError("Input.SpecificDates", "Enter at least one date (comma-separated, yyyy-MM-dd).");
            return Page();
        }

        var courtIds = Input.SelectedCourtIds ?? [];
        await _seriesService.CreateAsync(
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
            maxOccurrencesToGenerate: 200);

        TempData["StatusMessage"] = $"Recurring series \"{Input.Name}\" created successfully.";
        return RedirectToPage("SeriesIndex");
    }

    private async Task LoadCourtsAsync()
    {
        var courts = await _context.Courts
            .Where(c => c.Status == CourtStatus.Active)
            .OrderBy(c => c.Name)
            .ToListAsync();
        CourtOptions = new SelectList(courts, "Id", "Name");
    }

    public class SeriesInput
    {
        [Required][MaxLength(150)][Display(Name = "Series Name")]
        public string Name { get; set; } = string.Empty;

        [MaxLength(2000)][Display(Name = "Description")]
        public string? Description { get; set; }

        [Display(Name = "Format")]
        public ActivityFormat Format { get; set; } = ActivityFormat.OpenPlay;

        [Display(Name = "Skill Level")]
        public ActivitySkillLevel SkillLevel { get; set; } = ActivitySkillLevel.Open;

        [Display(Name = "Recurrence Type")]
        public RecurrenceType RecurrenceType { get; set; } = RecurrenceType.Weekly;

        [Display(Name = "Weekly Days (CSV of 0-6, Sunday=0)")]
        public string? WeeklyDays { get; set; }

        [Range(1, 31)][Display(Name = "Day of Month")]
        public int? MonthlyDayOfMonth { get; set; }

        [Display(Name = "Specific Dates (comma-separated yyyy-MM-dd)")]
        public string? SpecificDates { get; set; }

        [Required][Display(Name = "Series Start Date")]
        public string SeriesStartDateStr { get; set; } = string.Empty;

        [Display(Name = "Series End Date (leave blank for open-ended)")]
        public string? SeriesEndDateStr { get; set; }

        [Required][Display(Name = "Occurrence Start Time")]
        public string OccurrenceStartTimeStr { get; set; } = string.Empty;

        [Required][Display(Name = "Occurrence End Time")]
        public string OccurrenceEndTimeStr { get; set; } = string.Empty;

        [Range(0, 500)][Display(Name = "Max Capacity (0 = unlimited)")]
        public int MaxCapacity { get; set; } = 0;

        [Range(0, 100000)][Display(Name = "Price per Player (₱)")]
        public decimal PricePerPlayer { get; set; } = 0;

        [Display(Name = "Courts")]
        public List<int>? SelectedCourtIds { get; set; }
    }
}
