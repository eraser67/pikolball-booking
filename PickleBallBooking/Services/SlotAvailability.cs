namespace PickleBallBooking.Services;

/// <summary>
/// Represents the availability status of a single hourly TimeSlot for a court on a specific date.
/// </summary>
public class SlotAvailability
{
    public int TimeSlotId { get; set; }

    public TimeSpan StartTime { get; set; }

    public TimeSpan EndTime { get; set; }

    public bool IsAvailable { get; set; }

    public bool IsMaintenance { get; set; }
}
