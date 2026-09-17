using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Availability;

public class CourtAvailabilityStatus
{
    public Court Court { get; set; } = null!;
    public int AvailableSlots { get; set; }
    public int BookedSlots { get; set; }
    public int TotalSlots { get; set; }
    public decimal AvailabilityPercentage => TotalSlots > 0 ? (decimal)AvailableSlots / TotalSlots * 100 : 0;
    public string Status => AvailableSlots > 0 ? "Available" : "Fully Booked";
    public string StatusBadge => AvailableSlots > 0 ? "success" : "danger";
}

public class TimeSlotAvailability
{
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string Label { get; set; } = string.Empty;
    public string FormattedLabel { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public List<Court> AvailableCourts { get; set; } = new();
    public List<Court> BookedCourts { get; set; } = new();
    public bool HasAvailableSlots => AvailableCourts.Count > 0;
    public string CourtStatusText => HasAvailableSlots 
        ? $"{AvailableCourts.Count} court{(AvailableCourts.Count > 1 ? "s" : "")} available" 
        : "Fully booked";
}

public class IndexModel : PageModel
{
    private const int CalendarWindowDays = 7;

    private readonly IBookingService _bookingService;
    private readonly ICourtService _courtService;
    private readonly ITimeSlotService _timeSlotService;

    public IndexModel(IBookingService bookingService, ICourtService courtService, ITimeSlotService timeSlotService)
    {
        _bookingService = bookingService;
        _courtService = courtService;
        _timeSlotService = timeSlotService;
    }

    [BindProperty(SupportsGet = true)]
    public DateOnly Date { get; set; }

    public List<Court> Courts { get; set; } = new();

    public List<CourtAvailabilityStatus> CourtAvailabilities { get; set; } = new();

    public List<CalendarDateOption> DateOptions { get; set; } = new();

    public List<TimeSlotAvailability> TimeSlotAvailabilities { get; set; } = new();

    public bool IsPastDate { get; set; }

        public async Task OnGetAsync()
    {
        var today = AppClock.TodayLocal;

        if (Date == default)
        {
            Date = today;
        }

        IsPastDate = Date < today;

        Courts = await _courtService.GetActiveAsync();
        var timeSlots = await _timeSlotService.GetActiveAsync();

        DateOptions = Enumerable.Range(0, CalendarWindowDays)
            .Select(offset =>
            {
                var optionDate = today.AddDays(offset);
                return new CalendarDateOption(
                    optionDate,
                    optionDate.ToString("ddd"),
                    optionDate.ToString("MMM d"),
                    optionDate == Date);
            })
            .ToList();

        TimeSlotAvailabilities = new List<TimeSlotAvailability>();
        CourtAvailabilities = new List<CourtAvailabilityStatus>();

        if (timeSlots.Count == 0)
        {
            return;
        }

                // Use ONLY predefined time slots from the database (1-hour slots)
        // This prevents confusing combinations like 6-9 AM being "fully booked" when really only 6-7 is booked.
        // Availability for every court is fetched in a single round-trip to avoid N+1 queries.
        var availabilityByCourt = IsPastDate
            ? new Dictionary<int, List<SlotAvailability>>()
            : await _bookingService.GetAvailabilityForAllCourtsAsync(Courts.Select(c => c.Id), Date);

        foreach (var timeSlot in timeSlots.OrderBy(t => t.StartTime))
        {
            var priceResult = await _bookingService.CalculatePriceAsync(Date, timeSlot.StartTime, timeSlot.EndTime);
            var slot = new TimeSlotAvailability
            {
                StartTime = timeSlot.StartTime,
                EndTime = timeSlot.EndTime,
                Label = AppClock.To12HourRange(timeSlot.StartTime, timeSlot.EndTime),
                FormattedLabel = AppClock.To12HourRange(timeSlot.StartTime, timeSlot.EndTime),
                Price = priceResult.Success ? priceResult.Price : null
            };

            if (IsPastDate)
            {
                // Past dates have all courts booked
                slot.BookedCourts.AddRange(Courts);
            }
            else
            {
                foreach (var court in Courts)
                {
                    var isAvailable = availabilityByCourt.TryGetValue(court.Id, out var slots)
                        && slots.Any(s => s.TimeSlotId == timeSlot.Id && s.IsAvailable);

                    if (isAvailable)
                        slot.AvailableCourts.Add(court);
                    else
                        slot.BookedCourts.Add(court);
                }
            }

            TimeSlotAvailabilities.Add(slot);
        }

        // Calculate court availability for the selected date
        foreach (var court in Courts)
        {
            var availableCount = TimeSlotAvailabilities.Count(slot => slot.AvailableCourts.Contains(court));
            var bookedCount = TimeSlotAvailabilities.Count(slot => slot.BookedCourts.Contains(court));

            CourtAvailabilities.Add(new CourtAvailabilityStatus
            {
                Court = court,
                AvailableSlots = availableCount,
                BookedSlots = bookedCount,
                TotalSlots = TimeSlotAvailabilities.Count
            });
        }
    }

        public string GetAvailableCourtsList(List<Court> courts)
    {
        if (courts.Count == 0)
            return "No courts available";

        return string.Join(", ", courts.Select(c => c.Name));
    }
}

