using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Booking;

public class IndexModel : PageModel
{
    private const int CalendarWindowDays = 7;

    private readonly IBookingService _bookingService;
    private readonly ICourtService _courtService;
    private readonly ITimeSlotService _timeSlotService;
    private readonly ApplicationDbContext _context;

    public IndexModel(IBookingService bookingService, ICourtService courtService, ITimeSlotService timeSlotService, ApplicationDbContext context)
    {
        _bookingService = bookingService;
        _courtService = courtService;
        _timeSlotService = timeSlotService;
        _context = context;
    }

    [BindProperty]
    public BookingInput Input { get; set; } = new();

    public List<SelectListItem> Courts { get; set; } = new();

    public List<CalendarDateOption> DateOptions { get; set; } = new();

    /// <summary>
    /// List of all TimeSlots for the selected date/court, with availability status
    /// </summary>
    public List<TimeSlotAvailabilityView> TimeSlotAvailabilities { get; set; } = new();

    /// <summary>
    /// Array of selected TimeSlot IDs from the form submission
    /// </summary>
    [BindProperty]
    public int[] SelectedSlotIds { get; set; } = Array.Empty<int>();

    public decimal? CalculatedPrice { get; set; }

    public string? ErrorMessage { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(DateOnly? date, int? courtId)
    {
        await LoadOptionsAsync();
        ApplySelection(date, courtId);
        await LoadCalendarAsync();
        await LoadTimeSlotAvailabilityAsync();
    }

    public async Task<IActionResult> OnPostCalculateAsync()
    {
        await LoadOptionsAsync();
        await LoadCalendarAsync();
        await LoadTimeSlotAvailabilityAsync();

        // Clear customer info validation for price calculation
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.CustomerName)}");
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.CustomerPhone)}");
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.CustomerEmail)}");

        if (!ModelState.IsValid || SelectedSlotIds.Length == 0)
        {
            ErrorMessage = "Please select at least one time slot.";
            return Page();
        }

        await TryCalculatePriceAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync()
    {
        await LoadOptionsAsync();
        await LoadCalendarAsync();
        await LoadTimeSlotAvailabilityAsync();

        // Validate that at least one slot is selected
        if (SelectedSlotIds.Length == 0)
        {
            ModelState.AddModelError("", "Please select at least one time slot.");
        }

        // Validate continuity and availability before submission
        if (SelectedSlotIds.Length > 0)
        {
            var (isValid, errorMsg) = await ValidateSlotSelectionAsync(SelectedSlotIds);
            if (!isValid)
            {
                ModelState.AddModelError("", errorMsg);
            }
        }

        if (!ModelState.IsValid)
        {
            await TryCalculatePriceAsync();
            return Page();
        }

        // Get the slots to determine StartTime and EndTime
        var selectedSlots = _context.TimeSlots
            .Where(ts => SelectedSlotIds.Contains(ts.Id))
            .OrderBy(ts => ts.StartTime)
            .ToList();

        if (selectedSlots.Count == 0)
        {
            return Page();
        }

        var startTime = selectedSlots.First().StartTime;
        var endTime = selectedSlots.Last().EndTime;

        // Create booking using the slot-aware method
        var result = await _bookingService.CreateBookingWithSlotsAsync(
            Input.CourtId,
            Input.BookingDate,
            SelectedSlotIds.ToList(),
            Input.CustomerName,
            Input.CustomerPhone,
            Input.CustomerEmail);

        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage;
            await TryCalculatePriceAsync();
            return Page();
        }

        return RedirectToPage("Confirmation", new { reference = result.Booking!.BookingReference });
    }

    private void ApplySelection(DateOnly? date, int? courtId)
    {
        if (date.HasValue)
        {
            Input.BookingDate = date.Value;
        }

        if (courtId.HasValue)
        {
            Input.CourtId = courtId.Value;
        }
    }

    private async Task TryCalculatePriceAsync()
    {
        if (SelectedSlotIds.Length == 0)
        {
            ErrorMessage = "Please select at least one time slot.";
            return;
        }

        var (isValid, errorMsg) = await ValidateSlotSelectionAsync(SelectedSlotIds);
        if (!isValid)
        {
            ErrorMessage = errorMsg;
            return;
        }

        var selectedSlots = _context.TimeSlots
            .Where(ts => SelectedSlotIds.Contains(ts.Id))
            .OrderBy(ts => ts.StartTime)
            .ToList();

        if (selectedSlots.Count == 0)
        {
            ErrorMessage = "Selected time slots not found.";
            return;
        }

        var startTime = selectedSlots.First().StartTime;
        var endTime = selectedSlots.Last().EndTime;

                var result = await _bookingService.CalculatePriceAsync(Input.BookingDate, startTime, endTime, Input.CourtId);
        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage;
            return;
        }

        CalculatedPrice = result.Price;
    }

    /// <summary>
    /// Validates that selected slot IDs are:
    /// 1. Continuous (no gaps)
    /// 2. Available (not booked or maintenance)
    /// </summary>
    private async Task<(bool IsValid, string ErrorMessage)> ValidateSlotSelectionAsync(int[] slotIds)
    {
        if (slotIds.Length == 0)
        {
            return (false, "Please select at least one time slot.");
        }

        var selectedSlots = _context.TimeSlots
            .Where(ts => slotIds.Contains(ts.Id))
            .OrderBy(ts => ts.StartTime)
            .ToList();

                if (selectedSlots.Count != slotIds.Length)
        {
            return (false, "One or more selected time slots not found.");
        }

        // Reject any slot that has already started today (server-side enforcement;
        // the UI disables these, but a crafted request must not bypass it).
        if (Input.BookingDate == AppClock.TodayLocal)
        {
            var nowHours = AppClock.NowLocal.TimeOfDay.TotalHours;
            if (selectedSlots.Any(s => s.StartTime.TotalHours <= nowHours))
            {
                return (false, "One or more selected time slots have already passed.");
            }
        }

                // Check continuity. The final slot (23:00-00:00) ends at TimeSpan.Zero,
        // so treat a zero end time as the 24:00 boundary.
        for (int i = 0; i < selectedSlots.Count - 1; i++)
        {
            var previousEnd = selectedSlots[i].EndTime;
            if (previousEnd == TimeSpan.Zero)
            {
                previousEnd = TimeSpan.FromHours(24);
            }

            if (previousEnd != selectedSlots[i + 1].StartTime)
            {
                return (false, "Selected time slots must be continuous (no gaps allowed).");
            }
        }

        // Check availability using the booking service
        var startTime = selectedSlots.First().StartTime;
        var endTime = selectedSlots.Last().EndTime;
        var isAvailable = await _bookingService.IsAvailableAsync(Input.CourtId, Input.BookingDate, startTime, endTime);

        if (!isAvailable)
        {
            return (false, "One or more selected time slots are not available.");
        }

        return (true, "");
    }

    private async Task LoadOptionsAsync()
    {
        var courts = await _courtService.GetActiveAsync();
        Courts = courts
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
            .ToList();

        if (Input.CourtId == 0 && Courts.Count > 0)
        {
            Input.CourtId = int.Parse(Courts[0].Value!);
        }
    }

        private async Task LoadCalendarAsync()
    {
        var today = AppClock.TodayLocal;
        DateOptions = Enumerable.Range(0, CalendarWindowDays)
            .Select(offset =>
            {
                var optionDate = today.AddDays(offset);
                return new CalendarDateOption(
                    optionDate,
                    optionDate.ToString("ddd"),
                    optionDate.ToString("MMM d"),
                    optionDate == Input.BookingDate);
            })
            .ToList();
    }

    /// <summary>
    /// Loads all TimeSlots with their availability status for the selected court and date
    /// </summary>
    private async Task LoadTimeSlotAvailabilityAsync()
    {
        TimeSlotAvailabilities = new();

        if (Input.CourtId <= 0)
        {
            return;
        }

        var timeSlots = await _timeSlotService.GetActiveAsync();
        var availableSlots = await _bookingService.GetAvailableSlotsAsync(Input.CourtId, Input.BookingDate);

                var isToday = Input.BookingDate == AppClock.TodayLocal;
        var nowHours = AppClock.NowLocal.TimeOfDay.TotalHours;

        foreach (var slot in timeSlots.OrderBy(ts => ts.StartTime))
        {
            var availability = availableSlots.FirstOrDefault(a => a.TimeSlotId == slot.Id);
            var isSelected = SelectedSlotIds.Contains(slot.Id);
            var isAvailable = availability?.IsAvailable ?? false;
            var isMaintenance = availability?.IsMaintenance ?? false;

            // A slot is "past" when the booking is for today and the slot has already
            // started (its start time is at or before the current local time). Past
            // slots can never be selected or booked. Future-dated bookings are exempt.
            var isPast = isToday && slot.StartTime.TotalHours <= nowHours;

            // Past takes precedence over every other status so the UI cannot offer a
            // slot that the server would reject.
            var status = isPast
                ? "past"
                : isMaintenance
                    ? "maintenance"
                    : (isAvailable ? "available" : "booked");

            TimeSlotAvailabilities.Add(new TimeSlotAvailabilityView
            {
                TimeSlotId = slot.Id,
                StartTime = slot.StartTime,
                EndTime = slot.EndTime,
                DisplayTime = AppClock.To12HourRange(slot.StartTime, slot.EndTime),
                IsAvailable = isAvailable && !isPast,
                IsMaintenance = isMaintenance,
                IsPast = isPast,
                IsSelected = isSelected,
                Status = status
            });
        }
    }

    public class BookingInput
    {
        [Required(ErrorMessage = "Please select a booking date.")]
        [DataType(DataType.Date)]
                [Display(Name = "Booking Date")]
        public DateOnly BookingDate { get; set; } = AppClock.TodayLocal;

        [Range(1, int.MaxValue, ErrorMessage = "Please select a court.")]
        [Display(Name = "Court")]
        public int CourtId { get; set; }

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

    public class TimeSlotAvailabilityView
    {
        public int TimeSlotId { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string DisplayTime { get; set; } = "";
                public bool IsAvailable { get; set; }
        public bool IsMaintenance { get; set; }
        public bool IsPast { get; set; }
        public bool IsSelected { get; set; }
        public string Status { get; set; } = ""; // "available", "booked", "maintenance", "past"
    }
}

public class CalendarDateOption
{
    public DateOnly Date { get; set; }
    public string DayLabel { get; set; }
    public string DateLabel { get; set; }
    public bool IsSelected { get; set; }

    public CalendarDateOption(DateOnly date, string dayLabel, string dateLabel, bool isSelected)
    {
        Date = date;
        DayLabel = dayLabel;
        DateLabel = dateLabel;
        IsSelected = isSelected;
    }
}
