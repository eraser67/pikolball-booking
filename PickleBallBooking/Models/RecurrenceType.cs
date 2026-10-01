namespace PickleBallBooking.Models;

/// <summary>
/// Phase 35: defines how an ActivitySeries repeats.
/// </summary>
public enum RecurrenceType
{
    /// <summary>Repeats on selected days of the week (e.g. every Monday and Wednesday).</summary>
    Weekly = 0,

    /// <summary>Repeats on a specific day of the month (e.g. every 15th).</summary>
    Monthly = 1,

    /// <summary>Repeats on an explicit custom set of dates.</summary>
    SpecificDays = 2,
}
