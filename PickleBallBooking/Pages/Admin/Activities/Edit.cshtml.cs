using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Activities;

public class EditModel : PageModel
{
    private readonly ActivityService _activityService;
    private readonly ApplicationDbContext _context;

    public EditModel(ActivityService activityService, ApplicationDbContext context)
    {
        _activityService = activityService;
        _context = context;
    }

    [BindProperty] public int Id { get; set; }
    [BindProperty] public CreateModel.ActivityInput Input { get; set; } = new();
    public SelectList? CourtOptions { get; private set; }
    public Activity? Activity { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Activity = await _activityService.GetByIdAsync(id);
        if (Activity is null) return NotFound();
        Id = id;
        Input.Name                = Activity.Name;
        Input.Description         = Activity.Description;
        Input.Format              = Activity.Format;
        Input.Status              = Activity.Status;
        Input.DateStr             = Activity.Date.ToString("yyyy-MM-dd");
        Input.StartTimeStr        = Activity.StartTime.ToString(@"hh\:mm");
        Input.EndTimeStr          = Activity.EndTime.ToString(@"hh\:mm");
        Input.SkillLevel          = Activity.SkillLevel;
        Input.MaxCapacity         = Activity.MaxCapacity;
        Input.PricePerPlayer      = Activity.PricePerPlayer;
        Input.RegistrationOpensAt  = AppClock.ToPhilippineTime(Activity.RegistrationOpensAt);
        Input.RegistrationClosesAt = AppClock.ToPhilippineTime(Activity.RegistrationClosesAt);
        Input.SelectedCourtIds    = Activity.ActivityCourts.Select(ac => ac.CourtId).ToList();
        await LoadCourtsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadCourtsAsync();
        if (!ModelState.IsValid) return Page();

        if (!DateOnly.TryParse(Input.DateStr, out var date) ||
            !TimeSpan.TryParse(Input.StartTimeStr, out var start) ||
            !TimeSpan.TryParse(Input.EndTimeStr, out var end))
        {
            ModelState.AddModelError("", "Invalid date or time values.");
            return Page();
        }

        if (end <= start)
        {
            ModelState.AddModelError("Input.EndTimeStr", "End time must be after start time.");
            return Page();
        }

        var result = await _activityService.UpdateAsync(
            Id, Input.Name, Input.Description, Input.Format, date, start, end,
            Input.SkillLevel, Input.MaxCapacity, Input.PricePerPlayer,
            Input.RegistrationOpensAt, Input.RegistrationClosesAt,
            Input.SelectedCourtIds ?? [],
            Input.Status);

        if (result is null) return NotFound();
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostSetStatusAsync(int id, ActivityStatus status)
    {
        await _activityService.SetStatusAsync(id, status);
        return RedirectToPage("Index");
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
