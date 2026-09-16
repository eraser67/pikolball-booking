using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Schedule;

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

    public Dictionary<(int CourtId, int TimeSlotId), Models.Booking> BookingsByCourtAndSlot { get; set; } = new();

    public async Task OnGetAsync()
    {
        if (Date == default)
        {
            Date = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        }

        Courts = await _courtService.GetActiveAsync();
        TimeSlots = await _timeSlotService.GetActiveAsync();

        var bookings = await _bookingService.GetBookingsForAdminAsync(new BookingAdminFilter
        {
            BookingDate = Date
        });

        BookingsByCourtAndSlot = bookings
            .Where(b => b.BookingStatus != BookingStatus.Cancelled)
            .ToDictionary(b => (b.CourtId, b.TimeSlotId));
    }
}
