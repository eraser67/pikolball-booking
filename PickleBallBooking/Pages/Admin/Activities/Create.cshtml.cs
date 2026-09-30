using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Activities;

public class CreateModel : PageModel
{
    private readonly ActivityService _activityService;
    private readonly ApplicationDbContext _context;

    public CreateModel(ActivityService activityService, ApplicationDbContext context)
    {
        _activityService = activityService;
        _context = context;
    }

    [BindProperty] public ActivityInput Input { get; set; } = new();
    public SelectList? CourtOptions { get; private set; }

    public async Task OnGetAsync()
    {
        Input.DateStr = DateOnly.FromDateTime(DateTime.Today.AddDays(1)).ToString("yyyy-MM-dd");
        Input.StartTimeStr = "08:00";
        Input.EndTimeStr   = "10:00";
        await LoadCourtsAsync();
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

        var courtIds = Input.SelectedCourtIds ?? [];
        await _activityService.CreateAsync(
            Input.Name, Input.Description, Input.Format, date, start, end,
            Input.SkillLevel, Input.MaxCapacity, Input.PricePerPlayer,
            Input.RegistrationOpensAt, Input.RegistrationClosesAt, courtIds,
            Input.Status);

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

    public class ActivityInput
    {
        [Required][MaxLength(150)][Display(Name = "Activity Name")]
        public string Name { get; set; } = string.Empty;

        [MaxLength(2000)][Display(Name = "Description")]
        public string? Description { get; set; }

        [Display(Name = "Format")]
        public ActivityFormat Format { get; set; } = ActivityFormat.OpenPlay;

        [Display(Name = "Status")]
        public ActivityStatus Status { get; set; } = ActivityStatus.Published;

        [Required][Display(Name = "Date")]
        public string DateStr { get; set; } = string.Empty;

        [Required][Display(Name = "Start Time")]
        public string StartTimeStr { get; set; } = string.Empty;

        [Required][Display(Name = "End Time")]
        public string EndTimeStr { get; set; } = string.Empty;

        [Display(Name = "Skill Level")]
        public ActivitySkillLevel SkillLevel { get; set; } = ActivitySkillLevel.Open;

        [Range(0, 500)][Display(Name = "Max Capacity (0 = unlimited)")]
        public int MaxCapacity { get; set; } = 0;

        [Range(0, 100000)][Display(Name = "Price per Player (₱)")]
        public decimal PricePerPlayer { get; set; } = 0;

        [Display(Name = "Registration Opens")]
        public DateTime? RegistrationOpensAt { get; set; }

        [Display(Name = "Registration Closes")]
        public DateTime? RegistrationClosesAt { get; set; }

        [Display(Name = "Courts")]
        public List<int>? SelectedCourtIds { get; set; }
    }
}
