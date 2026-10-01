using System.ComponentModel.DataAnnotations;

namespace PickleBallBooking.Models;

/// <summary>
/// Phase 41: Audit record for any match score correction made after finalization.
/// Tracks previous scores, new scores, the administrator who performed the override,
/// and an explicit required reason.
/// </summary>
public class MatchScoreAudit
{
    public int Id { get; set; }

    public int OrganizationId { get; set; }

    public int RoundRobinMatchId { get; set; }

    public int? PreviousTeam1Score { get; set; }

    public int? PreviousTeam2Score { get; set; }

    [MaxLength(50)]
    public string? PreviousWinningSide { get; set; }

    [MaxLength(500)]
    public string? PreviousScoresJson { get; set; }

    public int NewTeam1Score { get; set; }

    public int NewTeam2Score { get; set; }

    [MaxLength(50)]
    public string? NewWinningSide { get; set; }

    [MaxLength(500)]
    public string? NewScoresJson { get; set; }

    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    [Required]
    [MaxLength(450)]
    public string ChangedByUserId { get; set; } = string.Empty;

    [MaxLength(200)]
    public string ChangedByUserName { get; set; } = string.Empty;

    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public RoundRobinMatch? Match { get; set; }
}
