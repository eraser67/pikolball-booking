using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public class TimeSlotService : ITimeSlotService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext? _tenantContext;

    public TimeSlotService(ApplicationDbContext context, ITenantContext? tenantContext = null)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    private int? CurrentOrgId => _tenantContext?.OrganizationId ?? _context.CurrentOrganizationId;

    public async Task<List<TimeSlot>> GetAllAsync()
    {
        var slots = await _context.TimeSlots
            .AsNoTracking()
            .OrderBy(t => t.StartTime)
            .Select(t => new TimeSlot
            {
                Id = t.Id,
                StartTime = t.StartTime,
                EndTime = t.EndTime,
                Status = t.Status
            })
            .ToListAsync();

        var orgId = CurrentOrgId;
        if (orgId.HasValue)
        {
            var overrides = await _context.TenantTimeSlots
                .AsNoTracking()
                .Where(tts => tts.OrganizationId == orgId.Value)
                .ToDictionaryAsync(tts => tts.TimeSlotId, tts => tts.Status);

            foreach (var slot in slots)
            {
                if (overrides.TryGetValue(slot.Id, out var tenantStatus))
                {
                    slot.Status = tenantStatus;
                }
            }
        }

        return slots;
    }

    public async Task<List<TimeSlot>> GetActiveAsync()
    {
        var all = await GetAllAsync();
        return all.Where(t => t.Status == TimeSlotStatus.Active).ToList();
    }

    public async Task<TimeSlot?> GetByIdAsync(int id)
    {
        var slot = await _context.TimeSlots
            .AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new TimeSlot
            {
                Id = t.Id,
                StartTime = t.StartTime,
                EndTime = t.EndTime,
                Status = t.Status
            })
            .FirstOrDefaultAsync();

        if (slot is null)
        {
            return null;
        }

        var orgId = CurrentOrgId;
        if (orgId.HasValue)
        {
            var overrideStatus = await _context.TenantTimeSlots
                .AsNoTracking()
                .Where(tts => tts.OrganizationId == orgId.Value && tts.TimeSlotId == id)
                .Select(tts => (TimeSlotStatus?)tts.Status)
                .FirstOrDefaultAsync();

            if (overrideStatus.HasValue)
            {
                slot.Status = overrideStatus.Value;
            }
        }

        return slot;
    }

    public async Task<TimeSlot> CreateAsync(TimeSpan startTime, TimeSpan endTime)
    {
        var timeSlot = new TimeSlot
        {
            StartTime = startTime,
            EndTime = endTime,
            Status = TimeSlotStatus.Active
        };

        _context.TimeSlots.Add(timeSlot);
        await _context.SaveChangesAsync();

        return timeSlot;
    }

    public async Task<bool> UpdateAsync(int id, TimeSpan startTime, TimeSpan endTime)
    {
        var timeSlot = await _context.TimeSlots.FindAsync(id);
        if (timeSlot is null)
        {
            return false;
        }

        timeSlot.StartTime = startTime;
        timeSlot.EndTime = endTime;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetStatusAsync(int id, TimeSlotStatus status)
    {
        var masterExists = await _context.TimeSlots.AnyAsync(t => t.Id == id);
        if (!masterExists)
        {
            return false;
        }

        var orgId = CurrentOrgId;
        if (!orgId.HasValue)
        {
            var masterSlot = await _context.TimeSlots.FindAsync(id);
            if (masterSlot is null) return false;
            masterSlot.Status = status;
            await _context.SaveChangesAsync();
            return true;
        }

        var tenantSlot = await _context.TenantTimeSlots
            .FirstOrDefaultAsync(tts => tts.TimeSlotId == id && tts.OrganizationId == orgId.Value);

        if (tenantSlot is null)
        {
            tenantSlot = new TenantTimeSlot
            {
                OrganizationId = orgId.Value,
                TimeSlotId = id,
                Status = status,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.TenantTimeSlots.Add(tenantSlot);
        }
        else
        {
            tenantSlot.Status = status;
            tenantSlot.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var timeSlot = await _context.TimeSlots.FindAsync(id);
        if (timeSlot is null)
        {
            return false;
        }

        var hasBookings = await _context.BookingTimeSlots.AnyAsync(bts => bts.TimeSlotId == id);
        if (hasBookings)
        {
            throw new InvalidOperationException(
                $"Time slot {AppClock.To12HourRange(timeSlot.StartTime, timeSlot.EndTime)} cannot be deleted because it has existing bookings. Deactivate it instead.");
        }

        var courtTimeSlots = await _context.CourtTimeSlots
            .Where(cts => cts.TimeSlotId == id)
            .ToListAsync();
        if (courtTimeSlots.Count > 0)
        {
            _context.CourtTimeSlots.RemoveRange(courtTimeSlots);
        }

        var tenantTimeSlots = await _context.TenantTimeSlots
            .IgnoreQueryFilters()
            .Where(tts => tts.TimeSlotId == id)
            .ToListAsync();
        if (tenantTimeSlots.Count > 0)
        {
            _context.TenantTimeSlots.RemoveRange(tenantTimeSlots);
        }

        _context.TimeSlots.Remove(timeSlot);
        await _context.SaveChangesAsync();
        return true;
    }
}
