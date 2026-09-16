namespace PickleBallBooking.Models;

public class TimeSlot
{
    public int Id { get; set; }

    public TimeSpan StartTime { get; set; }

    public TimeSpan EndTime { get; set; }

    public TimeSlotStatus Status { get; set; } = TimeSlotStatus.Active;
}
