using System.ComponentModel.DataAnnotations;

namespace PickleBallBooking.Models;

/// <summary>
/// Phase 40: Tenant-owned round robin tournament/event configuration and schedule within an activity.
/// </summary>
public class RoundRobinEvent
{
    public int Id { get; set; }

    public int OrganizationId { get; set; }

    public int ActivityId { get; set; }

    public RoundRobinFormat Format { get; set; } = RoundRobinFormat.RotatingPartners;

    public int NumberOfRounds { get; set; } = 3;

    public int MatchDurationMinutes { get; set; } = 15;

    public int BreakDurationMinutes { get; set; } = 5;

    public ScoringType ScoringType { get; set; } = ScoringType.RallyScoring;

    public int PointsToWin { get; set; } = 11;

    public bool WinByTwo { get; set; } = true;

    public bool IsLocked { get; set; }

    public DateTime? LockedAt { get; set; }

    [MaxLength(450)]
    public string? LockedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    // Navigation
    public Activity? Activity { get; set; }

    public ICollection<RoundRobinMatch> Matches { get; set; } = [];

    public ICollection<RoundRobinBye> Byes { get; set; } = [];
}
