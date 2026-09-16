using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Availability;

public class IndexModel : PageModel
{
    private readonly IBookingService _bookingService;
    private readonly ICourtService _courtService;
    private readonly ITimeSlotService _timeSlotService;

    public IndexModel(IBookingService bookingService, ICourtService courtService, ITimeSlotService timeSlotService)
    {
        _bookingService = bookingService;
        _courtService = courtService;
        _timeSlotService = timeSlotService;
    }

    [BindProperty(SupportsGet = true)]
    public DateOnly Date { get; set; }

    public List<Court> Courts { get; set; } = new();

    public List<TimeSlot> TimeSlots { get; set; } = new();

    public HashSet<(int CourtId, int? TimeSlotId)> BookedSlots { get; set; } = new();

    public bool IsPastDate { get; set; }

    public async Task OnGetAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        if (Date == default)
        {
            Date = today;
        }

        IsPastDate = Date < today;

        Courts = await _courtService.GetActiveAsync();
        TimeSlots = await _timeSlotService.GetActiveAsync();

        var bookings = await _bookingService.GetBookingsForAdminAsync(new BookingAdminFilter
        {
            BookingDate = Date
        });

        BookedSlots = bookings
            .Where(b => b.BookingStatus != BookingStatus.Cancelled)
            .Select(b => (b.CourtId, b.TimeSlotId))
            .ToHashSet();
    }

    public bool IsAvailable(int courtId, int timeSlotId)
    {
        return !IsPastDate && !BookedSlots.Contains((courtId, timeSlotId));
    }
}
