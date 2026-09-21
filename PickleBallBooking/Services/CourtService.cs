using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public class CourtService : ICourtService
{
    private readonly ApplicationDbContext _context;

    public CourtService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<Court>> GetAllAsync()
    {
        return await _context.Courts
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<List<Court>> GetActiveAsync()
    {
        return await _context.Courts
            .Where(c => c.Status == CourtStatus.Active)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<Court?> GetByIdAsync(int id)
    {
        // Phase 21: query filters scope this to the current organization. FirstOrDefault
        // is used (rather than Find, which can bypass query filters) so a court owned by
        // another organization is never returned.
        return await _context.Courts.FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<Court> CreateAsync(string name, string? description)
    {
        // Phase 21: the context's write guard stamps the current organization's id on
        // the new court. Callers cannot choose the tenant.
        var court = new Court
        {
            Name        = name,
            Description = description,
            Status      = CourtStatus.Active,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };

        _context.Courts.Add(court);
        await _context.SaveChangesAsync();

        // Phase 24: auto-initialize 24 CourtTimeSlot records (one per global hourly
        // TimeSlot) so the new court is immediately available for booking and maintenance
        // management. This mirrors the SQL seeding done in the Phase 21 migration for
        // existing courts. No second initialization mechanism is introduced.
        await InitializeCourtTimeSlotsAsync(court);

        return court;
    }

    public async Task<bool> UpdateAsync(int id, string name, string? description)
    {
        var court = await _context.Courts.FirstOrDefaultAsync(c => c.Id == id);
        if (court is null)
        {
            return false;
        }

        court.Name        = name;
        court.Description = description;
        court.UpdatedAt   = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetStatusAsync(int id, CourtStatus status)
    {
        var court = await _context.Courts.FirstOrDefaultAsync(c => c.Id == id);
        if (court is null)
        {
            return false;
        }

        court.Status    = status;
        court.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    // -----------------------------------------------------------------------
    // Phase 24: image path management
    // -----------------------------------------------------------------------

    public async Task<bool> SetImageAsync(int id, string imagePath)
    {
        // Tenant-scoped query filter ensures this court belongs to the current org.
        var court = await _context.Courts.FirstOrDefaultAsync(c => c.Id == id);
        if (court is null) return false;

        court.ImagePath = imagePath;
        court.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveImageAsync(int id)
    {
        var court = await _context.Courts.FirstOrDefaultAsync(c => c.Id == id);
        if (court is null) return false;

        court.ImagePath = null;
        court.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    // -----------------------------------------------------------------------

    public async Task<bool> DeleteAsync(int id)
    {
        var court = await _context.Courts.FirstOrDefaultAsync(c => c.Id == id);
        if (court is null)
        {
            return false;
        }

        var hasBookings = await _context.BookingTimeSlots.AnyAsync(bts => bts.CourtId == id)
            || await _context.Bookings.AnyAsync(b => b.CourtId == id);
        if (hasBookings)
        {
            throw new InvalidOperationException(
                $"Court '{court.Name}' cannot be deleted because it has existing bookings. Deactivate it instead.");
        }

        var courtTimeSlots = await _context.CourtTimeSlots
            .Where(cts => cts.CourtId == id)
            .ToListAsync();
        if (courtTimeSlots.Count > 0)
        {
            _context.CourtTimeSlots.RemoveRange(courtTimeSlots);
        }

        _context.Courts.Remove(court);
        await _context.SaveChangesAsync();
        return true;
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates one <see cref="CourtTimeSlot"/> per global <see cref="TimeSlot"/> for the
    /// newly created court. This matches the Phase 21 migration behaviour for pre-existing courts
    /// and ensures that new courts are immediately visible in availability and maintenance pages.
    /// </summary>
    private async Task InitializeCourtTimeSlotsAsync(Court court)
    {
        var timeSlots = await _context.TimeSlots
            .Where(ts => ts.Status == TimeSlotStatus.Active)
            .ToListAsync();

        if (timeSlots.Count == 0) return;

        var courtTimeSlots = timeSlots.Select(ts => new CourtTimeSlot
        {
            CourtId            = court.Id,
            TimeSlotId         = ts.Id,
            AvailabilityStatus = CourtTimeSlotStatus.Active,
            CreatedAt          = DateTime.UtcNow,
            UpdatedAt          = DateTime.UtcNow,
            // OrganizationId will be stamped by the write guard in SaveChangesAsync.
        }).ToList();

        _context.CourtTimeSlots.AddRange(courtTimeSlots);
        await _context.SaveChangesAsync();
    }
}
