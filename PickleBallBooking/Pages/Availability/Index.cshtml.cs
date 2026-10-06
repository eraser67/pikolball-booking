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
    public string Status => Court.Status == CourtStatus.Inactive ? "Unavailable" : (AvailableSlots > 0 ? "Available" : "Fully Booked");
    public string StatusBadge => Court.Status == CourtStatus.Inactive ? "secondary" : (AvailableSlots > 0 ? "success" : "danger");
    public bool IsInactive => Court.Status == CourtStatus.Inactive;
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
    /// <summary>
    /// Maps CourtId to privacy-safe display name (e.g. "John D.", "Maintenance", or "Reserved").
    /// </summary>
    public Dictionary<int, string> CourtBookers { get; set; } = new();
    /// <summary>
    /// Maps CourtId to maintenance note (court-level or slot-level).
    /// </summary>
    public Dictionary<int, string?> CourtNotes { get; set; } = new();

    public bool HasAvailableSlots => AvailableCourts.Count > 0;
    public string CourtStatusText => HasAvailableSlots 
        ? $"{AvailableCourts.Count} court{(AvailableCourts.Count > 1 ? "s" : "")} available" 
        : "Fully booked";

    public string TimePeriod => StartTime.Hours switch
    {
        < 12 => "morning",
        < 17 => "afternoon",
        _ => "evening"
    };

    public string GetBookerForCourt(int courtId) =>
        CourtBookers.TryGetValue(courtId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : "Reserved";

    public string? GetMaintenanceNoteForCourt(int courtId) =>
        CourtNotes.TryGetValue(courtId, out var note) ? note : null;
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

        Courts = await _courtService.GetAllAsync();
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

        var activeCourts = Courts.Where(c => c.Status == CourtStatus.Active).ToList();

        // Availability for active courts fetched in a single round-trip.
        var availabilityByCourt = IsPastDate || activeCourts.Count == 0
            ? new Dictionary<int, List<SlotAvailability>>()
            : await _bookingService.GetAvailabilityForAllCourtsAsync(activeCourts.Select(c => c.Id), Date);

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

            foreach (var court in Courts)
            {
                // Inactive courts are unavailable for all slots with their court-level maintenance note
                if (court.Status == CourtStatus.Inactive)
                {
                    slot.BookedCourts.Add(court);
                    slot.CourtBookers[court.Id] = "Maintenance";
                    slot.CourtNotes[court.Id] = court.MaintenanceNote;
                    continue;
                }

                if (IsPastDate)
                {
                    slot.BookedCourts.Add(court);
                    slot.CourtBookers[court.Id] = "Past";
                    continue;
                }

                var isAvailable = false;
                string? booker = null;
                string? maintNote = null;

                if (availabilityByCourt.TryGetValue(court.Id, out var slots))
                {
                    var matchingSlot = slots.FirstOrDefault(s => s.TimeSlotId == timeSlot.Id);
                    if (matchingSlot != null)
                    {
                        isAvailable = matchingSlot.IsAvailable;
                        booker = matchingSlot.BookedBy;
                        maintNote = matchingSlot.MaintenanceNote;
                    }
                }

                if (isAvailable)
                {
                    slot.AvailableCourts.Add(court);
                }
                else
                {
                    slot.BookedCourts.Add(court);
                    slot.CourtBookers[court.Id] = booker ?? "Reserved";
                    if (!string.IsNullOrWhiteSpace(maintNote))
                    {
                        slot.CourtNotes[court.Id] = maintNote;
                    }
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

