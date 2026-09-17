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

        /// <summary>
    /// Maps (CourtId, TimeSlotId) to booking for display.
    /// </summary>
    public Dictionary<(int CourtId, int TimeSlotId), Models.Booking> BookingsByCourtAndSlot { get; set; } = new();

    /// <summary>
    /// Slot availability matrix per court: courts[i][j] where i=courtId, j=slotId with SlotAvailability status.
    /// </summary>
    public Dictionary<int, List<SlotAvailability>> SlotAvailabilityByCourtId { get; set; } = new();

    public async Task OnGetAsync()
    {
        if (Date == default)
        {
            Date = AppClock.TodayLocal;
        }

        Courts = await _courtService.GetActiveAsync();
        TimeSlots = await _timeSlotService.GetActiveAsync();

        // Load slot-based bookings for the selected date.
        var bookings = await _bookingService.GetBookingsForAdminAsync(new BookingAdminFilter
        {
            BookingDate = Date
        });

        BookingsByCourtAndSlot = bookings
            .Where(b => b.BookingStatus != BookingStatus.Cancelled)
            .SelectMany(b => b.TimeSlots
                .Where(bts => bts.IsActive)
                .Select(bts => new { bts.CourtId, bts.TimeSlotId, Booking = b }))
            .GroupBy(x => (x.CourtId, x.TimeSlotId))
            .ToDictionary(g => g.Key, g => g.First().Booking);

        // Load slot-based availability for all courts in a single round-trip.
        SlotAvailabilityByCourtId = await _bookingService.GetAvailabilityForAllCourtsAsync(
            Courts.Select(c => c.Id),
            Date);
    }
}
