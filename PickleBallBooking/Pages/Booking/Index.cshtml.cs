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

    public List<SelectListItem> StartTimes { get; set; } = new();

    public List<SelectListItem> EndTimes { get; set; } = new();

    public decimal? CalculatedPrice { get; set; }

    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(DateOnly? date, int? courtId, TimeSpan? startTime, TimeSpan? endTime)
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

        if (startTime.HasValue)
        {
            Input.StartTime = startTime.Value;
        }

        if (endTime.HasValue)
        {
            Input.EndTime = endTime.Value;
        }

        if (courtId.HasValue && startTime.HasValue && endTime.HasValue && date.HasValue)
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
            var priceResult = await _bookingService.CalculatePriceAsync(Input.BookingDate, Input.StartTime!.Value, Input.EndTime!.Value);
            if (priceResult.Success)
            {
                CalculatedPrice = priceResult.Price;
            }

            return Page();
        }

        var result = await _bookingService.CreateBookingAsync(
            Input.CourtId,
            Input.BookingDate,
            Input.StartTime!.Value,
            Input.EndTime!.Value,
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
        var result = await _bookingService.CalculatePriceAsync(Input.BookingDate, Input.StartTime!.Value, Input.EndTime!.Value);
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
        var selectableTimes = timeSlots
            .SelectMany(t => new[] { t.StartTime, t.EndTime })
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        StartTimes = selectableTimes
            .Select(t => new SelectListItem(t.ToString(@"hh\:mm"), t.ToString(@"c")))
            .ToList();

        EndTimes = selectableTimes
            .Select(t => new SelectListItem(t.ToString(@"hh\:mm"), t.ToString(@"c")))
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

        [Required(ErrorMessage = "Please select a start time.")]
        [Display(Name = "Start Time")]
        [DataType(DataType.Time)]
        public TimeSpan? StartTime { get; set; }

        [Required(ErrorMessage = "Please select an end time.")]
        [Display(Name = "End Time")]
        [DataType(DataType.Time)]
        public TimeSpan? EndTime { get; set; }

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
