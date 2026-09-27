namespace PickleBallBooking.Models;

/// <summary>
/// Phase 33: lifecycle state of an Activity.
/// Transitions are managed by admin actions and (in Phase 35) by automated scheduling.
/// </summary>
public enum ActivityStatus
{
    /// <summary>Created by admin; not yet visible to players.</summary>
    Draft = 0,

    /// <summary>Visible to players; registration window has not opened yet.</summary>
    Published = 1,

    /// <summary>Registration window is open; players can join.</summary>
    RegistrationOpen = 2,

    /// <summary>Capacity reached; new joiners enter the waitlist (Phase 34).</summary>
    Full = 3,

    /// <summary>Registration period has ended; no new joiners.</summary>
    RegistrationClosed = 4,

    /// <summary>Activity is currently taking place.</summary>
    InProgress = 5,

    /// <summary>Activity has finished.</summary>
    Completed = 6,

    /// <summary>Cancelled by admin; all RSVPs are voided.</summary>
    Cancelled = 7,
}
