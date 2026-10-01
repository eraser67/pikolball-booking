namespace PickleBallBooking.Models;

/// <summary>
/// Phase 40 & 41: Lifecycle status of a competitive match.
/// </summary>
public enum MatchStatus
{
    Scheduled   = 0,
    InProgress  = 1,
    Completed   = 2,
    Cancelled   = 3,
}
