namespace PickleBallBooking.Models;

/// <summary>
/// Phase 38: Method used to check in a player or court booking attendee.
/// </summary>
public enum CheckInMethod
{
    /// <summary>Player presented a QR code on mobile scanned by venue staff.</summary>
    QrScan = 1,

    /// <summary>Staff manually marked attendance from admin dashboard roster/list.</summary>
    AdminManual = 2,

    /// <summary>Walk-in or verbal confirmation recorded by staff.</summary>
    WalkIn = 3,
}
