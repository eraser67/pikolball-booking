using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.TimeSlots;

public class CreateModel : PageModel
{
    private readonly ITimeSlotService _timeSlotService;

    public CreateModel(ITimeSlotService timeSlotService)
    {
        _timeSlotService = timeSlotService;
    }

    [BindProperty]
    public TimeSlotInput TimeSlot { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (TimeSlot.EndTime <= TimeSlot.StartTime)
        {
            ModelState.AddModelError(string.Empty, "End time must be after start time.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        await _timeSlotService.CreateAsync(TimeSlot.StartTime, TimeSlot.EndTime);

        return RedirectToPage("Index");
    }

    public class TimeSlotInput
    {
        [Required]
        [Display(Name = "Start Time")]
        [DataType(DataType.Time)]
        public TimeSpan StartTime { get; set; }

        [Required]
        [Display(Name = "End Time")]
        [DataType(DataType.Time)]
        public TimeSpan EndTime { get; set; }
    }
}
