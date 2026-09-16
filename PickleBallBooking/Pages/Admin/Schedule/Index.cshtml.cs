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
    /// Maps (CourtId, TimeSlotId) to booking for legacy range display.
    /// </summary>
    public Dictionary<(int CourtId, int? TimeSlotId), Models.Booking> BookingsByCourtAndSlot { get; set; } = new();

    /// <summary>
    /// Slot availability matrix per court: courts[i][j] where i=courtId, j=slotId with SlotAvailability status.
    /// </summary>
    public Dictionary<int, List<SlotAvailability>> SlotAvailabilityByCourtId { get; set; } = new();

    public async Task OnGetAsync()
    {
        if (Date == default)
        {
            Date = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        }

        Courts = await _courtService.GetActiveAsync();
        TimeSlots = await _timeSlotService.GetActiveAsync();

        // Load legacy range-based bookings for backward compatibility
        var bookings = await _bookingService.GetBookingsForAdminAsync(new BookingAdminFilter
        {
            BookingDate = Date
        });

        BookingsByCourtAndSlot = bookings
            .Where(b => b.BookingStatus != BookingStatus.Cancelled)
            .ToDictionary(b => (b.CourtId, b.TimeSlotId));

        // Load slot-based availability for each court (new fixed-slot model)
        SlotAvailabilityByCourtId = new Dictionary<int, List<SlotAvailability>>();
        foreach (var court in Courts)
        {
            var slotAvailability = await _bookingService.GetAvailableSlotsAsync(court.Id, Date);
            SlotAvailabilityByCourtId[court.Id] = slotAvailability;
        }
    }
}
