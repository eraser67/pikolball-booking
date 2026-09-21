namespace PickleBallBooking.Services;

/// <summary>
/// Central source of "now" for the application. All business logic that reasons
/// about the current date must go through this class so that the configured local
/// time zone (UTC+8) is applied consistently.
///
/// The database stores UTC timestamps (CreatedAt/UpdatedAt) but booking dates are
/// local (Philippines time, UTC+8). Keeping the two separate here avoids the
/// off-by-one-day bugs that come from mixing <see cref="DateTime.UtcNow"/> with a
/// local <see cref="DateOnly"/>.
/// </summary>
public static class AppClock
{
    /// <summary>
    /// The local time zone used for all booking-date calculations (UTC+8).
    /// </summary>
    public static readonly TimeZoneInfo LocalTimeZone = TimeZoneInfo.CreateCustomTimeZone(
        id: "Asia/Manila",
        baseUtcOffset: TimeSpan.FromHours(8),
        displayName: "(UTC+08:00) Manila",
        standardDisplayName: "Philippines Standard Time");

    /// <summary>
    /// The current date in the configured local time zone (UTC+8).
    /// </summary>
    public static DateOnly TodayLocal => DateOnly.FromDateTime(DateTime.UtcNow.Add(LocalTimeZone.BaseUtcOffset));

    /// <summary>
    /// The current local time (UTC+8) as a <see cref="DateTime"/>.
    /// </summary>
    public static DateTime NowLocal => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, LocalTimeZone);

    /// <summary>
    /// Converts a UTC <see cref="DateTime"/> (or DateTime with unspecified kind treated as UTC)
    /// to Philippines Standard Time (UTC+8).
    /// </summary>
    public static DateTime ToPhilippineTime(DateTime utcDateTime)
    {
        var utc = utcDateTime.Kind == DateTimeKind.Utc
            ? utcDateTime
            : DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, LocalTimeZone);
    }

    /// <summary>
    /// Converts a nullable UTC <see cref="DateTime"/> to Philippines Standard Time (UTC+8).
    /// </summary>
    public static DateTime? ToPhilippineTime(DateTime? utcDateTime)
    {
        return utcDateTime.HasValue ? ToPhilippineTime(utcDateTime.Value) : null;
    }

    /// <summary>
    /// Format a <see cref="TimeSpan"/> as a 12-hour clock label, e.g. "6:00 PM".
    /// Times of 24:00 (end-of-day) are rendered as "12:00 AM" of the next day's
    /// midnight boundary to keep overnight ranges readable.
    /// </summary>
    public static string To12Hour(TimeSpan time)
    {
        var normalized = Normalize(time);
        var dateTime = DateTime.Today.Add(normalized);
        return dateTime.ToString("h:mm tt");
    }

    /// <summary>
    /// Format a range as a 12-hour clock label, e.g. "6:00 PM - 8:00 PM".
    /// </summary>
    public static string To12HourRange(TimeSpan start, TimeSpan end)
        => $"{To12Hour(start)} - {To12Hour(end)}";

    /// <summary>
    /// Resolves a possibly overnight time range into a pair of monotonically
    /// increasing hour offsets. Because the final hourly slot ends at 00:00
    /// (e.g. 23:00-00:00), an end time that is less than or equal to the start
    /// time is interpreted as crossing midnight and shifted by 24 hours.
    /// </summary>
    public static (double StartHours, double EndHours) ToAbsoluteRange(TimeSpan startTime, TimeSpan endTime)
    {
        var start = startTime.TotalHours;
        var end = endTime.TotalHours;

        if (end <= start)
        {
            end += 24;
        }

        return (start, end);
    }

    /// <summary>
    /// Resolves a possibly overnight time range into absolute hour offsets,
    /// treating an end-of-day sentinel (23:59, 23:59:59, 24:00 or 00:00) as
    /// exactly 24:00 so that a range reaching the end of the day covers the
    /// full final hour (e.g. the 23:00-00:00 slot).
    /// </summary>
    public static (double StartHours, double EndHours) ToAbsoluteRangeNormalized(TimeSpan startTime, TimeSpan endTime)
    {
        var start = startTime.TotalHours;
        var end = SnapEndOfDay(endTime).TotalHours;

        if (end <= start)
        {
            end += 24;
        }

        return (start, end);
    }

    /// <summary>
    /// Snaps an "end of day" time to exactly 24:00. A time of 00:00, or anything
    /// within the final minute of the day (23:59:00-23:59:59.999), is treated as
    /// the 24:00 boundary. Other times are returned unchanged.
    /// </summary>
    public static TimeSpan SnapEndOfDay(TimeSpan time)
    {
        if (time <= TimeSpan.Zero)
        {
            return TimeSpan.FromHours(24);
        }

        if (time.TotalHours >= 24)
        {
            return TimeSpan.FromHours(24);
        }

        // Treat the 23:59 legacy "end of day" sentinel as midnight.
        if (time >= TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59))
        {
            return TimeSpan.FromHours(24);
        }

        return time;
    }

    /// <summary>
    /// Combines a booking date and an end time into a local <see cref="DateTime"/>.
    /// An end time that does not advance past the start time (or that reaches the
    /// end-of-day boundary, e.g. the 23:00-00:00 slot) is treated as crossing
    /// midnight, so the resulting instant falls on the following day.
    /// </summary>
    public static DateTime ToEndLocalDateTime(DateOnly date, TimeSpan startTime, TimeSpan endTime)
    {
        var end = SnapEndOfDay(endTime);
        if (end <= startTime)
        {
            // Genuine overnight range (e.g. 22:00-01:00): lands on the next day.
            end += TimeSpan.FromHours(24);
        }

        return date.ToDateTime(TimeOnly.MinValue).Add(end);
    }

    /// <summary>
    /// Normalize a <see cref="TimeSpan"/> to the 0..24h range, wrapping values
    /// such as 24:00:00 / 00:00:00 and rejecting negatives.
    /// </summary>
    public static TimeSpan Normalize(TimeSpan time)
    {
        var hours = time.TotalHours % 24;
        if (hours < 0)
        {
            hours += 24;
        }

        // Treat exactly 24:00 as the end-of-day boundary so it renders as 12:00 AM.
        if (time.TotalHours >= 24 && hours == 0)
        {
            return TimeSpan.FromHours(24);
        }

        return TimeSpan.FromHours(hours);
    }
}
