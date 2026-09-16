using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;

namespace PickleBallBooking.Pages.Booking;

public class ConfirmationModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public ConfirmationModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public Models.Booking? Booking { get; set; }

    public async Task OnGetAsync(string reference)
    {
        Booking = await _context.Bookings
            .Include(b => b.Court)
            .Include(b => b.TimeSlot)
            .FirstOrDefaultAsync(b => b.BookingReference == reference);
    }
}
