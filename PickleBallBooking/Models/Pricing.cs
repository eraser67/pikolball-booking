namespace PickleBallBooking.Models;

public class Pricing
{
    public int Id { get; set; }

    public DayType DayType { get; set; }

    public TimeSpan StartTime { get; set; }

    public TimeSpan EndTime { get; set; }

    public decimal Price { get; set; }

    public PricingStatus Status { get; set; } = PricingStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
