namespace PickleBallBooking.Models;

/// <summary>Phase 26: lifecycle status for an organization subscription.</summary>
public enum SubscriptionStatus
{
    /// <summary>Free trial period — full access, limited duration.</summary>
    Trial,

    /// <summary>Manually activated by platform admin — full access.</summary>
    Active,

    /// <summary>Subscription period has ended — bookings blocked.</summary>
    Expired,

    /// <summary>Manually suspended by platform admin — bookings blocked.</summary>
    Suspended,

    /// <summary>Subscription cancelled — terminal state.</summary>
    Cancelled
}
