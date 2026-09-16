namespace PickleBallBooking.Models;

public enum BookingAvailabilityState
{
    Available,
    Booked,
    Unavailable,
    Selected
}

public sealed record CalendarDateOption(
    DateOnly Date,
    string DayLabel,
    string DateLabel,
    bool IsSelected);

public sealed record CalendarRangeOption(
    TimeSpan StartTime,
    TimeSpan EndTime,
    string Label,
    decimal? Price,
    BookingAvailabilityState State,
    bool IsSelected);
