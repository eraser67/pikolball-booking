using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.TimeSlots;

public class EditModel : PageModel
{
    private readonly ITimeSlotService _timeSlotService;

    public EditModel(ITimeSlotService timeSlotService)
    {
        _timeSlotService = timeSlotService;
    }

    [BindProperty]
    public int Id { get; set; }

    [BindProperty]
    public TimeSlotInput TimeSlot { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var timeSlot = await _timeSlotService.GetByIdAsync(id);
        if (timeSlot is null)
        {
            return NotFound();
        }

        Id = timeSlot.Id;
        TimeSlot = new TimeSlotInput
        {
            StartTime = timeSlot.StartTime,
            EndTime = timeSlot.EndTime
        };

        return Page();
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

        var updated = await _timeSlotService.UpdateAsync(Id, TimeSlot.StartTime, TimeSlot.EndTime);
        if (!updated)
        {
            return NotFound();
        }

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
