using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Booking;

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

    [BindProperty]
    public BookingInput Input { get; set; } = new();

    public List<SelectListItem> Courts { get; set; } = new();

    public List<SelectListItem> TimeSlots { get; set; } = new();

    public decimal? CalculatedPrice { get; set; }

    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(DateOnly? date, int? courtId, int? timeSlotId)
    {
        await LoadOptionsAsync();

        if (date.HasValue)
        {
            Input.BookingDate = date.Value;
        }

        if (courtId.HasValue)
        {
            Input.CourtId = courtId.Value;
        }

        if (timeSlotId.HasValue)
        {
            Input.TimeSlotId = timeSlotId.Value;
        }

        // When arriving from the Court Availability page with a pre-selected
        // court/date/time slot, immediately show the calculated price. The
        // server still re-validates availability here and again on submit.
        if (courtId.HasValue && timeSlotId.HasValue && date.HasValue)
        {
            await TryCalculatePriceAsync();
        }
    }

    public async Task<IActionResult> OnPostCalculateAsync()
    {
        await LoadOptionsAsync();

        ModelState.Remove($"{nameof(Input)}.{nameof(Input.CustomerName)}");
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.CustomerPhone)}");
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.CustomerEmail)}");

        if (!ModelState.IsValid)
        {
            return Page();
        }

        await TryCalculatePriceAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync()
    {
        await LoadOptionsAsync();

        if (!ModelState.IsValid)
        {
            var priceResult = await _bookingService.CalculatePriceAsync(Input.CourtId, Input.TimeSlotId, Input.BookingDate);
            if (priceResult.Success)
            {
                CalculatedPrice = priceResult.Price;
            }

            return Page();
        }

        var result = await _bookingService.CreateBookingAsync(
            Input.CourtId,
            Input.TimeSlotId,
            Input.BookingDate,
            Input.CustomerName,
            Input.CustomerPhone,
            Input.CustomerEmail);

        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage;
            return Page();
        }

        return RedirectToPage("Confirmation", new { reference = result.Booking!.BookingReference });
    }

    private async Task<bool> TryCalculatePriceAsync()
    {
        var result = await _bookingService.CalculatePriceAsync(Input.CourtId, Input.TimeSlotId, Input.BookingDate);
        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage;
            return false;
        }

        CalculatedPrice = result.Price;
        return true;
    }

    private async Task LoadOptionsAsync()
    {
        var courts = await _courtService.GetActiveAsync();
        Courts = courts
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
            .ToList();

        var timeSlots = await _timeSlotService.GetActiveAsync();
        TimeSlots = timeSlots
            .Select(t => new SelectListItem($"{t.StartTime:hh\\:mm} - {t.EndTime:hh\\:mm}", t.Id.ToString()))
            .ToList();
    }

    public class BookingInput
    {
        [Required(ErrorMessage = "Please select a booking date.")]
        [DataType(DataType.Date)]
        [Display(Name = "Booking Date")]
        public DateOnly BookingDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [Required(ErrorMessage = "Please select a court.")]
        [Display(Name = "Court")]
        public int CourtId { get; set; }

        [Required(ErrorMessage = "Please select a time slot.")]
        [Display(Name = "Time Slot")]
        public int TimeSlotId { get; set; }

        [Required(ErrorMessage = "Please enter your name.")]
        [MaxLength(100)]
        [Display(Name = "Full Name")]
        public string CustomerName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your mobile number.")]
        [Phone]
        [MaxLength(20)]
        [Display(Name = "Mobile Number")]
        public string CustomerPhone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your email.")]
        [EmailAddress]
        [MaxLength(256)]
        [Display(Name = "Email")]
        public string CustomerEmail { get; set; } = string.Empty;
    }
}
