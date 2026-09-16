using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public interface ITimeSlotService
{
    Task<List<TimeSlot>> GetAllAsync();

    Task<List<TimeSlot>> GetActiveAsync();

    Task<TimeSlot?> GetByIdAsync(int id);

    Task<TimeSlot> CreateAsync(TimeSpan startTime, TimeSpan endTime);

    Task<bool> UpdateAsync(int id, TimeSpan startTime, TimeSpan endTime);

    Task<bool> SetStatusAsync(int id, TimeSlotStatus status);
}
