using System.ComponentModel.DataAnnotations;

namespace PickleBallBooking.Models;

/// <summary>
/// Phase 26: a SaaS subscription plan template.
/// Plans are global (not tenant-scoped). Platform admins create and manage plans;
/// organizations are assigned to plans by platform admins.
/// </summary>
public class SubscriptionPlan
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Display description shown to admins.</summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Price per billing period (display only — no billing integration).</summary>
    public decimal Price { get; set; }

    public BillingPeriod BillingPeriod { get; set; } = BillingPeriod.Monthly;

    /// <summary>Maximum number of courts the org can create. Null = unlimited.</summary>
    public int? MaxCourts { get; set; }

    /// <summary>Maximum bookings allowed per month. Null = unlimited.</summary>
    public int? MaxBookingsPerMonth { get; set; }

    /// <summary>
    /// Free-text feature list (newline-separated) shown on the subscription
    /// details panel. Example: "Unlimited bookings\nEmail notifications\nGCash payments"
    /// </summary>
    [MaxLength(2000)]
    public string? Features { get; set; }

    /// <summary>When false, this plan cannot be assigned to new organizations.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When true, orgs on this plan are never blocked regardless of dates.</summary>
    public bool IsFree { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<Subscription> Subscriptions { get; set; } = [];
}
