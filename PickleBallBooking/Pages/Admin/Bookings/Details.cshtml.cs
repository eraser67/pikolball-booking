using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Bookings;

public class DetailsModel : PageModel
{
    private readonly IBookingService _bookingService;

    public DetailsModel(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    public Models.Booking? Booking { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Booking = await _bookingService.GetBookingByIdAsync(id);

        if (Booking is null)
        {
            return NotFound();
        }

        return Page();
    }
}
