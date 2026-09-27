namespace PickleBallBooking.Models;

/// <summary>
/// Phase 32: initial self-assessed skill level for a player.
///
/// Future extensions (Phase 54+):
///   - DUPR numeric rating
///   - Admin-assigned rating
///   - Granular self-assessment (2.5, 3.0, 3.5, 4.0, 4.5, 5.0)
/// </summary>
public enum PlayerSkillLevel
{
    /// <summary>New to pickleball, learning fundamentals.</summary>
    Beginner = 0,

    /// <summary>Comfortable with play, developing consistency.</summary>
    Intermediate = 1,

    /// <summary>Competitive player, strong technique.</summary>
    Advanced = 2
}
