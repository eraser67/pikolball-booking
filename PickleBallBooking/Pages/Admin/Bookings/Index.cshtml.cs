using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Bookings;

public class IndexModel : PageModel
{
    private readonly IBookingService _bookingService;
    private readonly ICourtService _courtService;
    private readonly ApplicationDbContext? _context;

    public IndexModel(IBookingService bookingService, ICourtService courtService, ApplicationDbContext? context = null)
    {
        _bookingService = bookingService;
        _courtService = courtService;
        _context = context;
    }

    [BindProperty(SupportsGet = true)]
    public DateOnly? BookingDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? CourtId { get; set; }

    [BindProperty(SupportsGet = true)]
    public BookingStatus? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? CustomerSearch { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? QuickFilter { get; set; }

    public int TodayCount { get; set; }
    public int PendingCount { get; set; }
    public int ConfirmedTodayCount { get; set; }
    public int TotalMatchingCount => Bookings.Count;

    public List<Models.Booking> Bookings { get; set; } = new();

    public List<Court> Courts { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostConfirmAsync(int id)
    {
        await ChangeStatusAsync(id, BookingStatus.Confirmed);
        return RedirectToPageWithFilters();
    }

    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        await ChangeStatusAsync(id, BookingStatus.Cancelled);
        return RedirectToPageWithFilters();
    }

    public async Task<IActionResult> OnPostCompleteAsync(int id)
    {
        await ChangeStatusAsync(id, BookingStatus.Completed);
        return RedirectToPageWithFilters();
    }

    private async Task ChangeStatusAsync(int id, BookingStatus newStatus)
    {
        var result = await _bookingService.UpdateBookingStatusAsync(id, newStatus);
        if (result.Success)
        {
            StatusMessage = $"Booking '{result.Booking!.BookingReference}' has been updated to {newStatus}.";
        }
        else
        {
            ErrorMessage = result.ErrorMessage;
        }
    }

    private async Task LoadAsync()
    {
        // Automatically complete confirmed bookings whose date/time has passed so
        // admins don't have to mark them manually.
        await _bookingService.AutoCompleteExpiredBookingsAsync();

        Courts = await _courtService.GetAllAsync();

        var today = AppClock.TodayLocal;

        // Apply quick filter shortcuts if supplied
        if (!string.IsNullOrWhiteSpace(QuickFilter))
        {
            switch (QuickFilter.Trim().ToLowerInvariant())
            {
                case "pending":
                    Status = BookingStatus.Pending;
                    break;
                case "today":
                    BookingDate = today;
                    break;
                case "tomorrow":
                    BookingDate = today.AddDays(1);
                    break;
                case "confirmed":
                    Status = BookingStatus.Confirmed;
                    break;
            }
        }

        var filter = new BookingAdminFilter
        {
            BookingDate = BookingDate,
            CourtId = CourtId,
            Status = Status,
            CustomerSearch = CustomerSearch
        };

        Bookings = await _bookingService.GetBookingsForAdminAsync(filter);

        if (_context != null)
        {
            TodayCount = await _context.Bookings.CountAsync(b => b.BookingDate == today);
            PendingCount = await _context.Bookings.CountAsync(b => b.BookingStatus == BookingStatus.Pending);
            ConfirmedTodayCount = await _context.Bookings.CountAsync(b => b.BookingDate == today && b.BookingStatus == BookingStatus.Confirmed);
        }
        else
        {
            TodayCount = Bookings.Count(b => b.BookingDate == today);
            PendingCount = Bookings.Count(b => b.BookingStatus == BookingStatus.Pending);
            ConfirmedTodayCount = Bookings.Count(b => b.BookingDate == today && b.BookingStatus == BookingStatus.Confirmed);
        }
    }

    private IActionResult RedirectToPageWithFilters()
    {
        return RedirectToPage(new
        {
            BookingDate,
            CourtId,
            Status,
            CustomerSearch,
            QuickFilter
        });
    }
}
