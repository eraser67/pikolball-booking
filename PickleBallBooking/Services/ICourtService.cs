using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public interface ICourtService
{
    Task<List<Court>> GetAllAsync();

    Task<List<Court>> GetActiveAsync();

    Task<Court?> GetByIdAsync(int id);

    Task<Court> CreateAsync(string name, string? description);

    Task<bool> UpdateAsync(int id, string name, string? description);

    Task<bool> SetStatusAsync(int id, CourtStatus status);
}
