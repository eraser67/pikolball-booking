namespace PickleBallBooking.Models;

/// <summary>
/// Phase 36: Typed in-app notification events for player notifications.
/// </summary>
public enum AppNotificationType
{
    // ── Activity RSVP ─────────────────────────────────────────────────────────

    /// <summary>Player's RSVP was confirmed.</summary>
    ActivityRsvpConfirmed = 1,

    /// <summary>Player was placed on the waitlist.</summary>
    ActivityWaitlisted = 2,

    /// <summary>Player was promoted from the waitlist to confirmed.</summary>
    ActivityWaitlistPromoted = 3,

    /// <summary>Player's RSVP was cancelled (by the player or an admin).</summary>
    ActivityRsvpCancelled = 4,

    /// <summary>An activity the player RSVP'd for has been cancelled.</summary>
    ActivityCancelled = 5,

    /// <summary>Reminder sent N hours before an activity the player is registered for.</summary>
    ActivityReminder = 6,

    // ── Admin / Staff alerts ──────────────────────────────────────────────────

    /// <summary>A player registered for an activity (admin/staff alert).</summary>
    ActivityNewRegistration = 7,

    /// <summary>A player cancelled their RSVP for an activity (admin/staff alert).</summary>
    ActivityRsvpCancelledAdmin = 8,

    // ── Phase 38 Check-In ─────────────────────────────────────────────────────

    /// <summary>Player attendance was checked in on the day of the activity.</summary>
    ActivityCheckedIn = 9,

    // ── Phase 39 Court Assignment ─────────────────────────────────────────────

    /// <summary>Player was assigned to a court for an activity.</summary>
    ActivityCourtAssigned = 10,

    // ── Phase 40 Round Robin ──────────────────────────────────────────────────

    /// <summary>Round Robin match schedule was generated/published for an activity.</summary>
    ActivityRoundRobinScheduled = 11,

    // ── Phase 41 Match Scoring ────────────────────────────────────────────────

    /// <summary>A match score was recorded or updated.</summary>
    MatchScoreRecorded = 12,

    /// <summary>A match score was officially finalized by an administrator.</summary>
    MatchScoreFinalized = 13
}

