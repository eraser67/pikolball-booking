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
                Status = TimeSlotStatus.Active // Master slots default to Active
            })
            .ToListAsync();

        var orgId = CurrentOrgId;
        if (orgId.HasValue)
        {
            var overrides = await _context.TenantTimeSlots
                .AsNoTracking()
                .Where(tts => tts.OrganizationId == orgId.Value)
                .ToListAsync();

            var overrideDict = overrides.ToDictionary(tts => tts.TimeSlotId);

            var result = new List<TimeSlot>();
            foreach (var slot in slots)
            {
                if (overrideDict.TryGetValue(slot.Id, out var tenantOverride))
                {
                    if (tenantOverride.IsDeleted)
                    {
                        continue; // Hidden/deleted for this tenant
                    }

                    slot.Status = tenantOverride.Status;
                    if (tenantOverride.CustomStartTime.HasValue)
                    {
                        slot.StartTime = tenantOverride.CustomStartTime.Value;
                    }
                    if (tenantOverride.CustomEndTime.HasValue)
                    {
                        slot.EndTime = tenantOverride.CustomEndTime.Value;
                    }
                }

                result.Add(slot);
            }

            return result.OrderBy(t => t.StartTime).ToList();
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
                Status = TimeSlotStatus.Active
            })
            .FirstOrDefaultAsync();

        if (slot is null)
        {
            return null;
        }

        var orgId = CurrentOrgId;
        if (orgId.HasValue)
        {
            var tenantOverride = await _context.TenantTimeSlots
                .AsNoTracking()
                .Where(tts => tts.OrganizationId == orgId.Value && tts.TimeSlotId == id)
                .FirstOrDefaultAsync();

            if (tenantOverride != null)
            {
                if (tenantOverride.IsDeleted)
                {
                    return null; // Deleted for this tenant
                }

                slot.Status = tenantOverride.Status;
                if (tenantOverride.CustomStartTime.HasValue)
                {
                    slot.StartTime = tenantOverride.CustomStartTime.Value;
                }
                if (tenantOverride.CustomEndTime.HasValue)
                {
                    slot.EndTime = tenantOverride.CustomEndTime.Value;
                }
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
            masterSlot.StartTime = startTime;
            masterSlot.EndTime = endTime;
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
                CustomStartTime = startTime,
                CustomEndTime = endTime,
                Status = TimeSlotStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.TenantTimeSlots.Add(tenantSlot);
        }
        else
        {
            tenantSlot.CustomStartTime = startTime;
            tenantSlot.CustomEndTime = endTime;
            tenantSlot.UpdatedAt = DateTime.UtcNow;
        }

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
        var masterExists = await _context.TimeSlots.AnyAsync(t => t.Id == id);
        if (!masterExists)
        {
            return false;
        }

        var orgId = CurrentOrgId;
        if (!orgId.HasValue)
        {
            var timeSlot = await _context.TimeSlots.FindAsync(id);
            if (timeSlot is null) return false;

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

        var hasTenantBookings = await _context.BookingTimeSlots
            .AnyAsync(bts => bts.TimeSlotId == id && bts.OrganizationId == orgId.Value);
        if (hasTenantBookings)
        {
            var slot = await GetByIdAsync(id);
            var label = slot != null ? AppClock.To12HourRange(slot.StartTime, slot.EndTime) : id.ToString();
            throw new InvalidOperationException(
                $"Time slot {label} cannot be deleted because it has existing bookings for your organization. Deactivate it instead.");
        }

        var tenantCourtSlots = await _context.CourtTimeSlots
            .Where(cts => cts.TimeSlotId == id && cts.OrganizationId == orgId.Value)
            .ToListAsync();
        if (tenantCourtSlots.Count > 0)
        {
            _context.CourtTimeSlots.RemoveRange(tenantCourtSlots);
        }

        var tenantSlot = await _context.TenantTimeSlots
            .FirstOrDefaultAsync(tts => tts.TimeSlotId == id && tts.OrganizationId == orgId.Value);

        if (tenantSlot is null)
        {
            tenantSlot = new TenantTimeSlot
            {
                OrganizationId = orgId.Value,
                TimeSlotId = id,
                Status = TimeSlotStatus.Inactive,
                IsDeleted = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.TenantTimeSlots.Add(tenantSlot);
        }
        else
        {
            tenantSlot.IsDeleted = true;
            tenantSlot.Status = TimeSlotStatus.Inactive;
            tenantSlot.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return true;
    }
}
