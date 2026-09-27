namespace PickleBallBooking.Models;

/// <summary>
/// Phase 33: join table linking an Activity to one or more Courts.
/// Also tenant-owned (OrganizationId must match both activity and court).
/// </summary>
public class ActivityCourt
{
    public int Id { get; set; }

    public int OrganizationId { get; set; }

    public int ActivityId { get; set; }

    public int CourtId { get; set; }

    // Navigation
    public Activity? Activity { get; set; }
    public Court? Court { get; set; }
}
