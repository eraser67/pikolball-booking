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

    /// <summary>
    /// Updates the image storage path for a court that belongs to the current tenant.
    /// Returns false if the court is not found (tenant-scoped query filter applies).
    /// </summary>
    Task<bool> SetImageAsync(int id, string imagePath);

    /// <summary>
    /// Clears the image storage path for a court that belongs to the current tenant.
    /// Returns false if the court is not found.
    /// </summary>
    Task<bool> RemoveImageAsync(int id);

    /// <summary>
    /// Deletes a court. Returns false if the court does not exist.
    /// Throws <see cref="InvalidOperationException"/> if the court is referenced by
    /// existing bookings or court time-slot configuration.
    /// </summary>
    Task<bool> DeleteAsync(int id);
}
