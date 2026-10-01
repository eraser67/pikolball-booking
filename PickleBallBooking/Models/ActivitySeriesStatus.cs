namespace PickleBallBooking.Models;

/// <summary>
/// Phase 35: lifecycle state of a recurring ActivitySeries.
/// </summary>
public enum ActivitySeriesStatus
{
    /// <summary>Series is actively generating occurrences on schedule.</summary>
    Active = 0,

    /// <summary>Series is temporarily paused; new occurrences are not generated.</summary>
    Paused = 1,

    /// <summary>Series has been permanently cancelled.</summary>
    Cancelled = 2,

    /// <summary>Series has reached its end date; no more occurrences will be generated.</summary>
    Completed = 3,
}
