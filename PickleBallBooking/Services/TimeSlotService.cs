using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public class TimeSlotService : ITimeSlotService
{
    private readonly ApplicationDbContext _context;

    public TimeSlotService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<TimeSlot>> GetAllAsync()
    {
        return await _context.TimeSlots
            .OrderBy(t => t.StartTime)
            .ToListAsync();
    }

    public async Task<List<TimeSlot>> GetActiveAsync()
    {
        return await _context.TimeSlots
            .Where(t => t.Status == TimeSlotStatus.Active)
            .OrderBy(t => t.StartTime)
            .ToListAsync();
    }

    public async Task<TimeSlot?> GetByIdAsync(int id)
    {
        return await _context.TimeSlots.FindAsync(id);
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
        var timeSlot = await _context.TimeSlots.FindAsync(id);
        if (timeSlot is null)
        {
            return false;
        }

        timeSlot.Status = status;

        await _context.SaveChangesAsync();
        return true;
    }
}
