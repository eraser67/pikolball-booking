using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

/// <summary>
/// Phase 33: Activity CRUD and listing service.
///
/// All methods operate within the current tenant context (EF global query filter
/// on Activity and ActivityCourt scopes to CurrentOrganizationId automatically).
/// </summary>
public sealed class ActivityService
{
    private readonly ApplicationDbContext _context;

    public ActivityService(ApplicationDbContext context) => _context = context;

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Ensures a DateTime has DateTimeKind.Utc before being written to PostgreSQL.
    /// The datetime-local HTML input produces Kind=Unspecified; this treats
    /// such values as UTC (the server timezone is UTC in production).
    /// </summary>
    private static DateTime? ToUtc(DateTime? dt)
        => dt is null ? null
         : dt.Value.Kind == DateTimeKind.Utc ? dt
         : DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc);

    // ── Admin ─────────────────────────────────────────────────────────────

    /// <summary>Returns all activities for the current tenant, ordered by date desc.</summary>
    public async Task<List<Activity>> GetAllAsync()
        => await _context.Activities
            .Include(a => a.ActivityCourts).ThenInclude(ac => ac.Court)
            .OrderByDescending(a => a.Date).ThenBy(a => a.StartTime)
            .ToListAsync();

    /// <summary>Returns a single activity by id (within current tenant).</summary>
    public async Task<Activity?> GetByIdAsync(int id)
        => await _context.Activities
            .Include(a => a.ActivityCourts).ThenInclude(ac => ac.Court)
            .FirstOrDefaultAsync(a => a.Id == id);

    /// <summary>Creates a new activity and assigns the given court ids.</summary>
    public async Task<Activity> CreateAsync(
        string name,
        string? description,
        ActivityFormat format,
        DateOnly date,
        TimeSpan startTime,
        TimeSpan endTime,
        ActivitySkillLevel skillLevel,
        int maxCapacity,
        decimal pricePerPlayer,
        DateTime? registrationOpensAt,
        DateTime? registrationClosesAt,
        IEnumerable<int> courtIds)
    {
        var activity = new Activity
        {
            Name                  = name.Trim(),
            Description           = description?.Trim(),
            Format                = format,
            Date                  = date,
            StartTime             = startTime,
            EndTime               = endTime,
            SkillLevel            = skillLevel,
            MaxCapacity           = maxCapacity,
            PricePerPlayer        = pricePerPlayer,
            RegistrationOpensAt   = ToUtc(registrationOpensAt),
            RegistrationClosesAt  = ToUtc(registrationClosesAt),
            Status                = ActivityStatus.Draft,
            CreatedAt             = DateTime.UtcNow,
            UpdatedAt             = DateTime.UtcNow,
        };

        _context.Activities.Add(activity);
        await _context.SaveChangesAsync(); // save to get the Activity.Id

        await SetCourtsAsync(activity.Id, courtIds);
        return activity;
    }

    /// <summary>Updates activity fields and replaces its court assignments.</summary>
    public async Task<Activity?> UpdateAsync(
        int id,
        string name,
        string? description,
        ActivityFormat format,
        DateOnly date,
        TimeSpan startTime,
        TimeSpan endTime,
        ActivitySkillLevel skillLevel,
        int maxCapacity,
        decimal pricePerPlayer,
        DateTime? registrationOpensAt,
        DateTime? registrationClosesAt,
        IEnumerable<int> courtIds)
    {
        var activity = await _context.Activities.FirstOrDefaultAsync(a => a.Id == id);
        if (activity is null) return null;

        activity.Name                 = name.Trim();
        activity.Description          = description?.Trim();
        activity.Format               = format;
        activity.Date                 = date;
        activity.StartTime            = startTime;
        activity.EndTime              = endTime;
        activity.SkillLevel           = skillLevel;
        activity.MaxCapacity          = maxCapacity;
        activity.PricePerPlayer       = pricePerPlayer;
        activity.RegistrationOpensAt  = ToUtc(registrationOpensAt);
        activity.RegistrationClosesAt = ToUtc(registrationClosesAt);
        activity.UpdatedAt            = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await SetCourtsAsync(id, courtIds);
        return activity;
    }

    /// <summary>Changes only the status of an activity.</summary>
    public async Task<bool> SetStatusAsync(int id, ActivityStatus status)
    {
        var activity = await _context.Activities.FirstOrDefaultAsync(a => a.Id == id);
        if (activity is null) return false;
        activity.Status    = status;
        activity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary>Deletes an activity and cascades to ActivityCourt rows.</summary>
    public async Task<bool> DeleteAsync(int id)
    {
        var activity = await _context.Activities.FirstOrDefaultAsync(a => a.Id == id);
        if (activity is null) return false;
        _context.Activities.Remove(activity);
        await _context.SaveChangesAsync();
        return true;
    }

    // ── Player-facing ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns upcoming Published or RegistrationOpen activities visible to players.
    /// Ordered by date ascending.
    /// </summary>
    public async Task<List<Activity>> GetPublishedAsync()
        => await _context.Activities
            .Include(a => a.ActivityCourts).ThenInclude(ac => ac.Court)
            .Where(a => a.Status == ActivityStatus.Published
                     || a.Status == ActivityStatus.RegistrationOpen
                     || a.Status == ActivityStatus.Full
                     || a.Status == ActivityStatus.RegistrationClosed)
            .OrderBy(a => a.Date).ThenBy(a => a.StartTime)
            .ToListAsync();

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces the court assignments for the given activity.
    /// Deletes removed courts, adds new ones. EF write guard stamps OrganizationId.
    /// </summary>
    private async Task SetCourtsAsync(int activityId, IEnumerable<int> courtIds)
    {
        var existing = await _context.ActivityCourts
            .Where(ac => ac.ActivityId == activityId)
            .ToListAsync();

        _context.ActivityCourts.RemoveRange(existing);
        await _context.SaveChangesAsync();

        var newCourts = courtIds
            .Distinct()
            .Select(cid => new ActivityCourt
            {
                ActivityId = activityId,
                CourtId    = cid,
            });

        _context.ActivityCourts.AddRange(newCourts);
        await _context.SaveChangesAsync();
    }
}
