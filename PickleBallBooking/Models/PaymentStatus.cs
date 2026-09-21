namespace PickleBallBooking.Models;

public enum PaymentStatus
{
    /// <summary>Created with booking. Awaiting customer submission.</summary>
    Pending = 0,

    /// <summary>Customer provided a GCash reference number (+ optional proof screenshot).</summary>
    Submitted = 1,

    /// <summary>Admin confirmed payment received.</summary>
    Verified = 2,

    /// <summary>Admin rejected (wrong amount, fake reference, etc.).</summary>
    Rejected = 3,

    /// <summary>Booking was cancelled before payment was verified.</summary>
    Cancelled = 4
}
