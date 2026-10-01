using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickleBallBooking.Models;

/// <summary>
/// Phase 35: a recurring activity template owned by a tenant.
///
/// An ActivitySeries defines the recurrence schedule and shared defaults
/// (name, format, skill level, capacity, price, start/end times) for a
/// set of generated <see cref="Activity"/> instances (occurrences).
///
/// Generating occurrences is the responsibility of <c>ActivitySeriesService</c>.
/// Each generated <see cref="Activity"/> sets its <see cref="Activity.SeriesId"/>
/// back-reference so the series can be queried from the other direction.
///
/// Multi-tenant: scoped by OrganizationId via EF global query filter.
/// The existing fixed-booking engine (Court, Booking, TimeSlot) is NOT touched.
/// </summary>
public class ActivitySeries
{
    public int Id { get; set; }

    /// <summary>Tenant owner. Stamped automatically by the EF write guard.</summary>
    public int OrganizationId { get; set; }

    // ── Series Identity ───────────────────────────────────────────────────

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public ActivityFormat Format { get; set; } = ActivityFormat.OpenPlay;

    public ActivitySkillLevel SkillLevel { get; set; } = ActivitySkillLevel.Open;

    // ── Recurrence ────────────────────────────────────────────────────────

    public RecurrenceType RecurrenceType { get; set; } = RecurrenceType.Weekly;

    /// <summary>
    /// For <see cref="RecurrenceType.Weekly"/>: a comma-separated list of
    /// <see cref="System.DayOfWeek"/> integer values (0 = Sunday … 6 = Saturday).
    /// Example: "1,3" = every Monday and Wednesday.
    /// Null/empty for non-weekly types.
    /// </summary>
    [MaxLength(20)]
    public string? WeeklyDays { get; set; }

    /// <summary>
    /// For <see cref="RecurrenceType.Monthly"/>: the day-of-month (1-31).
    /// Null for non-monthly types.
    /// </summary>
    public int? MonthlyDayOfMonth { get; set; }

    /// <summary>
    /// For <see cref="RecurrenceType.SpecificDays"/>: a comma-separated list
    /// of ISO dates (yyyy-MM-dd). Null/empty for non-specific-days types.
    /// </summary>
    [MaxLength(2000)]
    public string? SpecificDates { get; set; }

    // ── Scheduling Window ─────────────────────────────────────────────────

    /// <summary>First date an occurrence can be generated on.</summary>
    public DateOnly SeriesStartDate { get; set; }

    /// <summary>Last date an occurrence can be generated on. Null = open-ended.</summary>
    public DateOnly? SeriesEndDate { get; set; }

    // ── Occurrence Defaults ───────────────────────────────────────────────

    [Column(TypeName = "time without time zone")]
    public TimeSpan OccurrenceStartTime { get; set; }

    [Column(TypeName = "time without time zone")]
    public TimeSpan OccurrenceEndTime { get; set; }

    /// <summary>Maximum players per occurrence. 0 = unlimited.</summary>
    public int MaxCapacity { get; set; } = 0;

    /// <summary>Price per player in Philippine Peso. 0 = free.</summary>
    [Column(TypeName = "numeric(8,2)")]
    public decimal PricePerPlayer { get; set; } = 0;

    // ── Status ────────────────────────────────────────────────────────────

    public ActivitySeriesStatus Status { get; set; } = ActivitySeriesStatus.Active;

    // ── Timestamps ────────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ── Navigation ────────────────────────────────────────────────────────

    /// <summary>All Activity instances (occurrences) generated from this series.</summary>
    public ICollection<Activity> Occurrences { get; set; } = new List<Activity>();

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the parsed weekly days of week. Empty if none configured or type is not Weekly.
    /// </summary>
    public IReadOnlyList<DayOfWeek> GetWeeklyDays()
    {
        if (string.IsNullOrWhiteSpace(WeeklyDays))
            return Array.Empty<DayOfWeek>();

        return WeeklyDays
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var d) ? (DayOfWeek?)d : null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .Distinct()
            .OrderBy(d => (int)d)
            .ToList();
    }

    /// <summary>
    /// Returns the parsed specific dates list. Empty if none configured or type is not SpecificDays.
    /// </summary>
    public IReadOnlyList<DateOnly> GetSpecificDates()
    {
        if (string.IsNullOrWhiteSpace(SpecificDates))
            return Array.Empty<DateOnly>();

        return SpecificDates
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => DateOnly.TryParse(s, out var d) ? (DateOnly?)d : null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
    }

    /// <summary>
    /// Enumerates all dates within the series window that match the recurrence rule,
    /// up to <paramref name="maxDates"/> safety cap. Dates before today or before
    /// <see cref="SeriesStartDate"/> are excluded.
    /// </summary>
    public IEnumerable<DateOnly> EnumerateScheduledDates(DateOnly from, int maxDates = 730)
    {
        var count = 0;
        var end = SeriesEndDate;

        switch (RecurrenceType)
        {
            case RecurrenceType.Weekly:
                var weeklyDays = GetWeeklyDays();
                if (!weeklyDays.Any()) yield break;

                var cursor = from;
                while (count < maxDates && (!end.HasValue || cursor <= end.Value))
                {
                    if (weeklyDays.Contains(cursor.DayOfWeek))
                    {
                        yield return cursor;
                        count++;
                    }
                    cursor = cursor.AddDays(1);
                }
                break;

            case RecurrenceType.Monthly:
                if (!MonthlyDayOfMonth.HasValue) yield break;
                var dayOfMonth = MonthlyDayOfMonth.Value;

                var monthCursor = new DateOnly(from.Year, from.Month, 1);
                while (count < maxDates && (!end.HasValue || monthCursor <= (end.HasValue ? end.Value : DateOnly.MaxValue)))
                {
                    // Clamp to valid days in the month
                    var daysInMonth = DateTime.DaysInMonth(monthCursor.Year, monthCursor.Month);
                    var clampedDay = Math.Min(dayOfMonth, daysInMonth);
                    var candidate = new DateOnly(monthCursor.Year, monthCursor.Month, clampedDay);

                    if (candidate >= from && (!end.HasValue || candidate <= end.Value))
                    {
                        yield return candidate;
                        count++;
                    }
                    monthCursor = monthCursor.AddMonths(1);
                }
                break;

            case RecurrenceType.SpecificDays:
                foreach (var date in GetSpecificDates())
                {
                    if (date >= from && (!end.HasValue || date <= end.Value))
                    {
                        yield return date;
                        count++;
                        if (count >= maxDates) yield break;
                    }
                }
                break;
        }
    }
}
