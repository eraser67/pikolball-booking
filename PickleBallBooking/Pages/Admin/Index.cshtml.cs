using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Pages.Admin;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public IndexModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public int TodaysBookingsCount { get; set; }

    public int PendingBookingsCount { get; set; }

    public int ConfirmedBookingsCount { get; set; }

    public int CompletedBookingsCount { get; set; }

    public int ActiveCourtsCount { get; set; }

    public async Task OnGetAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        TodaysBookingsCount = await _context.Bookings.CountAsync(b => b.BookingDate == today);
        PendingBookingsCount = await _context.Bookings.CountAsync(b => b.BookingStatus == BookingStatus.Pending);
        ConfirmedBookingsCount = await _context.Bookings.CountAsync(b => b.BookingStatus == BookingStatus.Confirmed);
        CompletedBookingsCount = await _context.Bookings.CountAsync(b => b.BookingStatus == BookingStatus.Completed);
        ActiveCourtsCount = await _context.Courts.CountAsync(c => c.Status == CourtStatus.Active);
    }
}
