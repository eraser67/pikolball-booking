namespace PickleBallBooking.Models;

/// <summary>
/// Phase 40: Supported round robin formats.
/// </summary>
public enum RoundRobinFormat
{
    /// <summary>Each round, players are rearranged into new doubles pairs.</summary>
    RotatingPartners = 0,

    /// <summary>Doubles pairs remain constant; face different opponents each round.</summary>
    FixedPartners = 1,

    /// <summary>Individual players rotate opponents (1 vs 1).</summary>
    Singles = 2,
}
