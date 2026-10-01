using System.ComponentModel.DataAnnotations;

namespace PickleBallBooking.Models;

/// <summary>
/// Phase 40 & 41: Individual round robin match within a round robin event.
/// </summary>
public class RoundRobinMatch
{
    public int Id { get; set; }

    public int OrganizationId { get; set; }

    public int RoundRobinEventId { get; set; }

    public int RoundNumber { get; set; }

    public int? CourtId { get; set; }

    [MaxLength(100)]
    public string? CourtName { get; set; }

    public TimeSpan? EstimatedStartTime { get; set; }

    public TimeSpan? EstimatedEndTime { get; set; }

    public MatchStatus Status { get; set; } = MatchStatus.Scheduled;

    // Team 1 / Side 1
    public int? Team1Player1RsvpId { get; set; }
    [MaxLength(450)]
    public string? Team1Player1UserId { get; set; }
    [MaxLength(200)]
    public string Team1Player1Name { get; set; } = string.Empty;

    public int? Team1Player2RsvpId { get; set; }
    [MaxLength(450)]
    public string? Team1Player2UserId { get; set; }
    [MaxLength(200)]
    public string? Team1Player2Name { get; set; }

    // Team 2 / Side 2
    public int? Team2Player1RsvpId { get; set; }
    [MaxLength(450)]
    public string? Team2Player1UserId { get; set; }
    [MaxLength(200)]
    public string Team2Player1Name { get; set; } = string.Empty;

    public int? Team2Player2RsvpId { get; set; }
    [MaxLength(450)]
    public string? Team2Player2UserId { get; set; }
    [MaxLength(200)]
    public string? Team2Player2Name { get; set; }

    // Scores (Ready for Phase 41 Match Scoring)
    public int? Team1Score { get; set; }
    public int? Team2Score { get; set; }

    [MaxLength(500)]
    public string? ScoresJson { get; set; }

    [MaxLength(50)]
    public string? WinningSide { get; set; } // "Team1", "Team2", "Draw"

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    // Navigation
    public RoundRobinEvent? RoundRobinEvent { get; set; }
    public Court? Court { get; set; }
}
