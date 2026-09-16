using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.TimeSlots;

public class IndexModel : PageModel
{
    private readonly ITimeSlotService _timeSlotService;

    public IndexModel(ITimeSlotService timeSlotService)
    {
        _timeSlotService = timeSlotService;
    }

    public List<TimeSlot> TimeSlots { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        TimeSlots = await _timeSlotService.GetAllAsync();
    }

    public async Task<IActionResult> OnPostToggleStatusAsync(int id)
    {
        var timeSlot = await _timeSlotService.GetByIdAsync(id);
        if (timeSlot is null)
        {
            return NotFound();
        }

        var newStatus = timeSlot.Status == TimeSlotStatus.Active ? TimeSlotStatus.Inactive : TimeSlotStatus.Active;
        await _timeSlotService.SetStatusAsync(id, newStatus);

        StatusMessage = $"Time slot '{timeSlot.StartTime:hh\\:mm}-{timeSlot.EndTime:hh\\:mm}' has been {(newStatus == TimeSlotStatus.Active ? "activated" : "deactivated")}.";

        return RedirectToPage();
    }
}
