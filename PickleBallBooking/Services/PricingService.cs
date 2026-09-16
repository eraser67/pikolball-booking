using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public class PricingService : IPricingService
{
    private readonly ApplicationDbContext _context;

    public PricingService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<Pricing>> GetAllAsync()
    {
        return await _context.Pricings
            .OrderBy(p => p.DayType)
            .ThenBy(p => p.StartTime)
            .ToListAsync();
    }

    public async Task<List<Pricing>> GetActiveAsync()
    {
        return await _context.Pricings
            .Where(p => p.Status == PricingStatus.Active)
            .OrderBy(p => p.DayType)
            .ThenBy(p => p.StartTime)
            .ToListAsync();
    }

    public async Task<Pricing?> GetByIdAsync(int id)
    {
        return await _context.Pricings.FindAsync(id);
    }

    public async Task<Pricing> CreateAsync(DayType dayType, TimeSpan startTime, TimeSpan endTime, decimal price)
    {
        var pricing = new Pricing
        {
            DayType = dayType,
            StartTime = startTime,
            EndTime = endTime,
            Price = price,
            Status = PricingStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Pricings.Add(pricing);
        await _context.SaveChangesAsync();

        return pricing;
    }

    public async Task<bool> UpdateAsync(int id, DayType dayType, TimeSpan startTime, TimeSpan endTime, decimal price)
    {
        var pricing = await _context.Pricings.FindAsync(id);
        if (pricing is null)
        {
            return false;
        }

        pricing.DayType = dayType;
        pricing.StartTime = startTime;
        pricing.EndTime = endTime;
        pricing.Price = price;
        pricing.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetStatusAsync(int id, PricingStatus status)
    {
        var pricing = await _context.Pricings.FindAsync(id);
        if (pricing is null)
        {
            return false;
        }

        pricing.Status = status;
        pricing.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }
}
