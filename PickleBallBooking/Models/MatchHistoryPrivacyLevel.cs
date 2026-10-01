namespace PickleBallBooking.Models;

/// <summary>
/// Phase 43: Controls player match history and statistics visibility.
/// </summary>
public enum MatchHistoryPrivacyLevel
{
    Public = 0,        // Visible to everyone (public visitors and members)
    FollowersOnly = 1, // Visible to logged-in platform members / followers
    Private = 2        // Visible only to the player themselves and venue admins
}
