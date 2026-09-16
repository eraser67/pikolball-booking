using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public interface IPricingService
{
    Task<List<Pricing>> GetAllAsync();

    Task<List<Pricing>> GetActiveAsync();

    Task<Pricing?> GetByIdAsync(int id);

    Task<Pricing> CreateAsync(DayType dayType, TimeSpan startTime, TimeSpan endTime, decimal price);

    Task<bool> UpdateAsync(int id, DayType dayType, TimeSpan startTime, TimeSpan endTime, decimal price);

    Task<bool> SetStatusAsync(int id, PricingStatus status);
}
