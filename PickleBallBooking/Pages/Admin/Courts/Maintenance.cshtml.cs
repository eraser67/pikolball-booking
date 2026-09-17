using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Courts;

public class MaintenanceModel : PageModel
{
    private readonly ICourtService _courtService;
    private readonly ITimeSlotService _timeSlotService;
    private readonly ApplicationDbContext _context;

    public MaintenanceModel(ICourtService courtService, ITimeSlotService timeSlotService, ApplicationDbContext context)
    {
        _courtService = courtService;
        _timeSlotService = timeSlotService;
        _context = context;
    }

    [BindProperty(SupportsGet = true)]
    public int? SelectedCourtId { get; set; }

        [BindProperty(SupportsGet = true)]
    public DateOnly Date { get; set; } = AppClock.TodayLocal;

    public List<Court> Courts { get; set; } = new();

    public List<TimeSlot> TimeSlots { get; set; } = new();

    /// <summary>
    /// Per-court maintenance status: Courts[i][j] = true means TimeSlots[j] is in maintenance for Courts[i]
    /// </summary>
    public Dictionary<(int CourtId, int TimeSlotId), CourtTimeSlotStatus> MaintenanceMatrix { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        Courts = await _courtService.GetActiveAsync();
        TimeSlots = await _timeSlotService.GetActiveAsync();

                if (Date == default)
        {
            Date = AppClock.TodayLocal;
        }

        // Load court-timeslot maintenance status
        var courtTimeSlots = _context.CourtTimeSlots
            .Where(cts => !SelectedCourtId.HasValue || cts.CourtId == SelectedCourtId.Value)
            .ToList();

        foreach (var cts in courtTimeSlots)
        {
            MaintenanceMatrix[(cts.CourtId, cts.TimeSlotId)] = cts.AvailabilityStatus;
        }
    }

    public async Task<IActionResult> OnPostToggleMaintenanceAsync(int courtId, int timeSlotId)
    {
        var courtTimeSlot = _context.CourtTimeSlots
            .FirstOrDefault(cts => cts.CourtId == courtId && cts.TimeSlotId == timeSlotId);

        if (courtTimeSlot is null)
        {
            // Create if doesn't exist
            courtTimeSlot = new CourtTimeSlot
            {
                CourtId = courtId,
                TimeSlotId = timeSlotId,
                AvailabilityStatus = CourtTimeSlotStatus.Maintenance,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.CourtTimeSlots.Add(courtTimeSlot);
        }
        else
        {
            // Toggle between Active and Maintenance
            courtTimeSlot.AvailabilityStatus = courtTimeSlot.AvailabilityStatus == CourtTimeSlotStatus.Active
                ? CourtTimeSlotStatus.Maintenance
                : CourtTimeSlotStatus.Active;
            courtTimeSlot.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        var court = await _courtService.GetByIdAsync(courtId);
        var timeSlot = await _timeSlotService.GetByIdAsync(timeSlotId);

        var slotLabel = timeSlot is null
            ? "(unknown)"
            : AppClock.To12HourRange(timeSlot.StartTime, timeSlot.EndTime);
        StatusMessage = $"{court?.Name} at {slotLabel} is now {courtTimeSlot.AvailabilityStatus}.";

        return RedirectToPage(new { selectedCourtId = SelectedCourtId, date = Date.ToString("yyyy-MM-dd") });
    }
}
