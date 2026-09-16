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
        return await _context.Courts.FindAsync(id);
    }

    public async Task<Court> CreateAsync(string name, string? description)
    {
        var court = new Court
        {
            Name = name,
            Description = description,
            Status = CourtStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Courts.Add(court);
        await _context.SaveChangesAsync();

        return court;
    }

    public async Task<bool> UpdateAsync(int id, string name, string? description)
    {
        var court = await _context.Courts.FindAsync(id);
        if (court is null)
        {
            return false;
        }

        court.Name = name;
        court.Description = description;
        court.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetStatusAsync(int id, CourtStatus status)
    {
        var court = await _context.Courts.FindAsync(id);
        if (court is null)
        {
            return false;
        }

        court.Status = status;
        court.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }
}
