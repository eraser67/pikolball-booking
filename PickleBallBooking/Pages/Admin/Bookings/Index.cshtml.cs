using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Bookings;

public class IndexModel : PageModel
{
    private readonly IBookingService _bookingService;
    private readonly ICourtService _courtService;

    public IndexModel(IBookingService bookingService, ICourtService courtService)
    {
        _bookingService = bookingService;
        _courtService = courtService;
    }

    [BindProperty(SupportsGet = true)]
    public DateOnly? BookingDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? CourtId { get; set; }

    [BindProperty(SupportsGet = true)]
    public BookingStatus? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? CustomerSearch { get; set; }

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
        Courts = await _courtService.GetAllAsync();

        var filter = new BookingAdminFilter
        {
            BookingDate = BookingDate,
            CourtId = CourtId,
            Status = Status,
            CustomerSearch = CustomerSearch
        };

        Bookings = await _bookingService.GetBookingsForAdminAsync(filter);
    }

    private IActionResult RedirectToPageWithFilters()
    {
        return RedirectToPage(new
        {
            BookingDate,
            CourtId,
            Status,
            CustomerSearch
        });
    }
}
