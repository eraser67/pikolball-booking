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

    [TempData]
    public string? ErrorMessage { get; set; }

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
        return await ChangeStatusAsync(id, BookingStatus.Cancelled);
    }

    public async Task<IActionResult> OnPostConfirmAsync(int id)
    {
        return await ChangeStatusAsync(id, BookingStatus.Confirmed);
    }

    public async Task<IActionResult> OnPostCompleteAsync(int id)
    {
        return await ChangeStatusAsync(id, BookingStatus.Completed);
    }

    private async Task<IActionResult> ChangeStatusAsync(int id, BookingStatus newStatus)
    {
        Booking = await _bookingService.GetBookingByIdAsync(id);

        if (Booking is null)
        {
            return NotFound();
        }

        var result = await _bookingService.UpdateBookingStatusAsync(id, newStatus);

        if (result.Success)
        {
            StatusMessage = $"Booking {Booking.BookingReference} has been updated to {newStatus}.";
        }
        else
        {
            // Surface the reason (e.g. invalid status transition) to the user.
            ErrorMessage = result.ErrorMessage;
        }

        return RedirectToPage(new { id });
    }
}


