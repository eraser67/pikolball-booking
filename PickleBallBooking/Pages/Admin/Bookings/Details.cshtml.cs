using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Bookings;

public class DetailsModel : PageModel
{
    private readonly IBookingService _bookingService;
    private readonly ICourtService _courtService;
    private readonly ITimeSlotService _timeSlotService;
    private readonly ApplicationDbContext _context;

    public DetailsModel(IBookingService bookingService, ICourtService courtService, ITimeSlotService timeSlotService, ApplicationDbContext context)
    {
        _bookingService = bookingService;
        _courtService = courtService;
        _timeSlotService = timeSlotService;
        _context = context;
    }

    public Models.Booking? Booking { get; set; }

    /// <summary>
    /// Slot details for fixed-slot bookings (if any).
    /// </summary>
    public List<(BookingTimeSlot Slot, TimeSlot TimeSlot)> BookingSlots { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
    {
        // Complete any expired confirmed bookings so the details view is accurate.
        await _bookingService.AutoCompleteExpiredBookingsAsync();

        Booking = await _bookingService.GetBookingByIdAsync(id);

        if (Booking is null)
        {
            return NotFound();
        }

        // Load any associated slots (for fixed-slot bookings)
        var slots = _context.BookingTimeSlots
            .Where(bts => bts.BookingId == id && bts.IsActive)
            .ToList();

        foreach (var slot in slots)
        {
            var timeSlot = await _timeSlotService.GetByIdAsync(slot.TimeSlotId);
            if (timeSlot is not null)
            {
                BookingSlots.Add((slot, timeSlot));
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        Booking = await _bookingService.GetBookingByIdAsync(id);

        if (Booking is null)
        {
            return NotFound();
        }

        if (Booking.BookingStatus == BookingStatus.Cancelled)
        {
            StatusMessage = "This booking is already cancelled.";
            return RedirectToPage(new { id });
        }

        // Use slot-aware cancellation method if available
        await _bookingService.CancelBookingAsync(id);

        StatusMessage = $"Booking {Booking.BookingReference} has been cancelled successfully.";
        return RedirectToPage();
    }
}
