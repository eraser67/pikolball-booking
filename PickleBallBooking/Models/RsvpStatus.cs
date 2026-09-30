namespace PickleBallBooking.Models;

/// <summary>
/// Phase 34: lifecycle state of a player's RSVP to an activity.
/// </summary>
public enum RsvpStatus
{
    /// <summary>Player has submitted a join request (pending admin or auto-confirm).</summary>
    Requested = 0,

    /// <summary>Player's spot is confirmed.</summary>
    Confirmed = 1,

    /// <summary>Activity is full; player is on the waitlist.</summary>
    Waitlisted = 2,

    /// <summary>Player cancelled or admin removed them.</summary>
    Cancelled = 3,

    /// <summary>Player was checked in on the day of the activity (Phase 38).</summary>
    CheckedIn = 4,

    /// <summary>Player did not appear (Phase 38).</summary>
    NoShow = 5,
}
