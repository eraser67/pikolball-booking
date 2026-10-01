using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

/// <summary>
/// Phase 35: Recurring Activity Series management service.
///
/// Responsibilities:
/// - CRUD for ActivitySeries templates (tenant-scoped).
/// - Generating Activity occurrences from a series for upcoming dates.
/// - Series lifecycle: pause, resume, cancel, cancel-from-date.
/// - Cancelling a single occurrence (without touching its siblings).
/// - Modifying future occurrences (capacity, price, courts).
///
/// Design guarantee: zero impact on the fixed-booking engine.
/// The service only creates Activity records (Phase 33) — it never
/// touches Booking, TimeSlot, CourtTimeSlot, or BookingTimeSlot.
/// </summary>
public sealed class ActivitySeriesService
{
    private readonly ApplicationDbContext _context;
    private readonly ActivityService _activityService;

    public ActivitySeriesService(ApplicationDbContext context, ActivityService activityService)
    {
        _context = context;
        _activityService = activityService;
    }

    // ── Admin read operations ─────────────────────────────────────────────

    /// <summary>Returns all series for the current tenant, ordered by start date desc.</summary>
    public async Task<List<ActivitySeries>> GetAllAsync()
        => await _context.ActivitySeries
            .OrderByDescending(s => s.SeriesStartDate)
            .ToListAsync();

    /// <summary>Returns a single series by id (within current tenant).</summary>
    public async Task<ActivitySeries?> GetByIdAsync(int id)
        => await _context.ActivitySeries
            .Include(s => s.Occurrences)
            .FirstOrDefaultAsync(s => s.Id == id);

    // ── CRUD ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new ActivitySeries template and immediately generates all
    /// Activity occurrences from <paramref name="seriesStartDate"/> up to
    /// <paramref name="seriesEndDate"/> (or up to 2 years ahead if open-ended),
    /// limited to the next <paramref name="maxOccurrencesToGenerate"/> occurrences.
    /// </summary>
    public async Task<ActivitySeries> CreateAsync(
        string name,
        string? description,
        ActivityFormat format,
        ActivitySkillLevel skillLevel,
        RecurrenceType recurrenceType,
        string? weeklyDays,
        int? monthlyDayOfMonth,
        string? specificDates,
        DateOnly seriesStartDate,
        DateOnly? seriesEndDate,
        TimeSpan occurrenceStartTime,
        TimeSpan occurrenceEndTime,
        int maxCapacity,
        decimal pricePerPlayer,
        IEnumerable<int> courtIds,
        int maxOccurrencesToGenerate = 200)
    {
        var series = new ActivitySeries
        {
            Name                  = name.Trim(),
            Description           = description?.Trim(),
            Format                = format,
            SkillLevel            = skillLevel,
            RecurrenceType        = recurrenceType,
            WeeklyDays            = NormaliseWeeklyDays(weeklyDays),
            MonthlyDayOfMonth     = monthlyDayOfMonth,
            SpecificDates         = NormaliseSpecificDates(specificDates),
            SeriesStartDate       = seriesStartDate,
            SeriesEndDate         = seriesEndDate,
            OccurrenceStartTime   = occurrenceStartTime,
            OccurrenceEndTime     = occurrenceEndTime,
            MaxCapacity           = maxCapacity,
            PricePerPlayer        = pricePerPlayer,
            Status                = ActivitySeriesStatus.Active,
            CreatedAt             = DateTime.UtcNow,
            UpdatedAt             = DateTime.UtcNow,
        };

        _context.ActivitySeries.Add(series);
        await _context.SaveChangesAsync();

        // Generate initial occurrences
        await GenerateOccurrencesAsync(series, seriesStartDate, courtIds, maxOccurrencesToGenerate);

        return series;
    }

    /// <summary>
    /// Updates a series template. Does NOT retroactively alter already-generated
    /// past occurrences. Future occurrences that have not yet started are updated
    /// if <paramref name="applyToFutureOccurrences"/> is true.
    /// </summary>
    public async Task<ActivitySeries?> UpdateAsync(
        int id,
        string name,
        string? description,
        ActivityFormat format,
        ActivitySkillLevel skillLevel,
        RecurrenceType recurrenceType,
        string? weeklyDays,
        int? monthlyDayOfMonth,
        string? specificDates,
        DateOnly seriesStartDate,
        DateOnly? seriesEndDate,
        TimeSpan occurrenceStartTime,
        TimeSpan occurrenceEndTime,
        int maxCapacity,
        decimal pricePerPlayer,
        IEnumerable<int> courtIds,
        bool applyToFutureOccurrences = false)
    {
        var series = await _context.ActivitySeries
            .Include(s => s.Occurrences)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (series is null) return null;

        series.Name                = name.Trim();
        series.Description         = description?.Trim();
        series.Format              = format;
        series.SkillLevel          = skillLevel;
        series.RecurrenceType      = recurrenceType;
        series.WeeklyDays          = NormaliseWeeklyDays(weeklyDays);
        series.MonthlyDayOfMonth   = monthlyDayOfMonth;
        series.SpecificDates       = NormaliseSpecificDates(specificDates);
        series.SeriesStartDate     = seriesStartDate;
        series.SeriesEndDate       = seriesEndDate;
        series.OccurrenceStartTime = occurrenceStartTime;
        series.OccurrenceEndTime   = occurrenceEndTime;
        series.MaxCapacity         = maxCapacity;
        series.PricePerPlayer      = pricePerPlayer;
        series.UpdatedAt           = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        if (applyToFutureOccurrences)
        {
            var today = AppClock.TodayLocal;
            var courtIdList = courtIds.ToList();
            var futureOccurrences = series.Occurrences
                .Where(a => a.Date >= today
                            && a.Status is ActivityStatus.Draft
                                        or ActivityStatus.Published
                                        or ActivityStatus.RegistrationOpen)
                .ToList();

            foreach (var occurrence in futureOccurrences)
            {
                await _activityService.UpdateAsync(
                    occurrence.Id,
                    series.Name,
                    series.Description,
                    series.Format,
                    occurrence.Date,       // keep original date
                    series.OccurrenceStartTime,
                    series.OccurrenceEndTime,
                    series.SkillLevel,
                    series.MaxCapacity,
                    series.PricePerPlayer,
                    occurrence.RegistrationOpensAt,
                    occurrence.RegistrationClosesAt,
                    courtIdList);
            }
        }

        return series;
    }

    // ── Series Lifecycle ─────────────────────────────────────────────────

    /// <summary>Pauses a series; new occurrences are not generated while paused.</summary>
    public async Task<bool> PauseAsync(int id)
        => await SetSeriesStatusAsync(id, ActivitySeriesStatus.Paused);

    /// <summary>Resumes a paused series and regenerates any missing future occurrences.</summary>
    public async Task<(bool success, int occurrencesGenerated)> ResumeAsync(int id, IEnumerable<int> courtIds)
    {
        var series = await _context.ActivitySeries
            .Include(s => s.Occurrences)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (series is null || series.Status != ActivitySeriesStatus.Paused)
            return (false, 0);

        series.Status    = ActivitySeriesStatus.Active;
        series.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var today = AppClock.TodayLocal;
        var generated = await GenerateOccurrencesAsync(series, today, courtIds);
        return (true, generated);
    }

    /// <summary>
    /// Cancels the entire series and all future non-completed occurrences.
    /// Past (Completed/InProgress) occurrences are preserved.
    /// </summary>
    public async Task<(bool success, int occurrencesCancelled)> CancelSeriesAsync(int id)
    {
        var series = await _context.ActivitySeries
            .Include(s => s.Occurrences)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (series is null) return (false, 0);

        series.Status    = ActivitySeriesStatus.Cancelled;
        series.UpdatedAt = DateTime.UtcNow;

        var today = AppClock.TodayLocal;
        var futureCancellable = series.Occurrences
            .Where(a => a.Date >= today
                        && a.Status is not ActivityStatus.Completed
                                   and not ActivityStatus.Cancelled)
            .ToList();

        foreach (var occ in futureCancellable)
        {
            occ.Status    = ActivityStatus.Cancelled;
            occ.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return (true, futureCancellable.Count);
    }

    /// <summary>
    /// Cancels the series from a given date onward — all occurrences on or after
    /// <paramref name="fromDate"/> that are not yet completed are cancelled.
    /// The series itself is also marked Cancelled.
    /// </summary>
    public async Task<(bool success, int occurrencesCancelled)> CancelSeriesFromDateAsync(int id, DateOnly fromDate)
    {
        var series = await _context.ActivitySeries
            .Include(s => s.Occurrences)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (series is null) return (false, 0);

        series.Status    = ActivitySeriesStatus.Cancelled;
        series.UpdatedAt = DateTime.UtcNow;

        var toCancel = series.Occurrences
            .Where(a => a.Date >= fromDate
                        && a.Status is not ActivityStatus.Completed
                                   and not ActivityStatus.Cancelled)
            .ToList();

        foreach (var occ in toCancel)
        {
            occ.Status    = ActivityStatus.Cancelled;
            occ.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return (true, toCancel.Count);
    }

    // ── Single Occurrence Operations ──────────────────────────────────────

    /// <summary>
    /// Cancels a single occurrence without affecting its siblings.
    /// </summary>
    public async Task<bool> CancelOccurrenceAsync(int activityId)
    {
        var activity = await _context.Activities
            .FirstOrDefaultAsync(a => a.Id == activityId && a.SeriesId != null);

        if (activity is null) return false;

        activity.Status    = ActivityStatus.Cancelled;
        activity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    // ── Occurrence Generation ─────────────────────────────────────────────

    /// <summary>
    /// Generates Activity occurrences for the series starting from <paramref name="from"/>.
    /// Skips dates that already have an occurrence.
    /// Returns the number of new occurrences created.
    /// </summary>
    public async Task<int> GenerateOccurrencesAsync(
        ActivitySeries series,
        DateOnly from,
        IEnumerable<int> courtIds,
        int maxOccurrences = 200)
    {
        // Get already-generated dates to avoid duplicates
        var existingDates = series.Occurrences
            .Select(o => o.Date)
            .ToHashSet();

        var scheduledDates = series
            .EnumerateScheduledDates(from, maxDates: maxOccurrences * 2)
            .Where(d => !existingDates.Contains(d))
            .Take(maxOccurrences)
            .ToList();

        var courtIdList = courtIds.ToList();
        var created = 0;

        foreach (var date in scheduledDates)
        {
            var activity = await _activityService.CreateAsync(
                name:                 series.Name,
                description:          series.Description,
                format:               series.Format,
                date:                 date,
                startTime:            series.OccurrenceStartTime,
                endTime:              series.OccurrenceEndTime,
                skillLevel:           series.SkillLevel,
                maxCapacity:          series.MaxCapacity,
                pricePerPlayer:       series.PricePerPlayer,
                registrationOpensAt:  null,
                registrationClosesAt: null,
                courtIds:             courtIdList,
                status:               ActivityStatus.Published);

            // Stamp the series back-reference (ActivityService.CreateAsync doesn't know about SeriesId)
            activity.SeriesId = series.Id;
            activity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Keep in-memory collection consistent for duplicate-check in next loop
            series.Occurrences.Add(activity);
            created++;
        }

        return created;
    }

    // ── Private Helpers ───────────────────────────────────────────────────

    private async Task<bool> SetSeriesStatusAsync(int id, ActivitySeriesStatus status)
    {
        var series = await _context.ActivitySeries.FirstOrDefaultAsync(s => s.Id == id);
        if (series is null) return false;
        series.Status    = status;
        series.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary>Normalises weekly days CSV: parse, dedup, sort, rejoin.</summary>
    private static string? NormaliseWeeklyDays(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parsed = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var d) ? (int?)d : null)
            .Where(d => d.HasValue && d.Value is >= 0 and <= 6)
            .Select(d => d!.Value)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
        return parsed.Any() ? string.Join(",", parsed) : null;
    }

    /// <summary>Normalises specific dates CSV: parse, dedup, sort, rejoin.</summary>
    private static string? NormaliseSpecificDates(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parsed = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => DateOnly.TryParse(s, out var d) ? (DateOnly?)d : null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
        return parsed.Any() ? string.Join(",", parsed.Select(d => d.ToString("yyyy-MM-dd"))) : null;
    }

    /// <summary>
    /// Returns the court ids currently assigned to any (most-recently-updated) occurrence
    /// of the series. Used to pre-fill UI defaults on edit.
    /// </summary>
    public async Task<List<int>> GetCurrentCourtIdsAsync(int seriesId)
    {
        var recentOccurrence = await _context.Activities
            .Include(a => a.ActivityCourts)
            .Where(a => a.SeriesId == seriesId)
            .OrderByDescending(a => a.UpdatedAt)
            .FirstOrDefaultAsync();

        return recentOccurrence?.ActivityCourts.Select(ac => ac.CourtId).ToList() ?? [];
    }
}
