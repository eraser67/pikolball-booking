using System.ComponentModel.DataAnnotations;

namespace PickleBallBooking.Models;

/// <summary>
/// Phase 40: Tracks which players have a bye (sit out) in each round robin round.
/// </summary>
public class RoundRobinBye
{
    public int Id { get; set; }

    public int OrganizationId { get; set; }

    public int RoundRobinEventId { get; set; }

    public int RoundNumber { get; set; }

    public int ActivityRsvpId { get; set; }

    [MaxLength(450)]
    public string? UserId { get; set; }

    [MaxLength(200)]
    public string PlayerName { get; set; } = string.Empty;

    // Navigation
    public RoundRobinEvent? RoundRobinEvent { get; set; }
}
