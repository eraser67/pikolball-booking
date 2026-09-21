using System.ComponentModel.DataAnnotations;

namespace PickleBallBooking.Models;

/// <summary>
/// Phase 26: an organization's subscription to a plan.
/// One active subscription per organization at any time.
/// Created/managed exclusively by platform admins — organizations cannot self-subscribe.
/// </summary>
public class Subscription
{
    public int Id { get; set; }

    /// <summary>The tenant this subscription belongs to.</summary>
    public int OrganizationId { get; set; }

    /// <summary>The plan the org is subscribed to.</summary>
    public int PlanId { get; set; }

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Trial;

    /// <summary>When the active subscription period starts. Null = not yet started.</summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>
    /// When the subscription expires. Null = no fixed end date (e.g. monthly auto-renew
    /// managed manually). An Expired status should be set manually when this passes.
    /// </summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>When the trial period ends. Null = no trial.</summary>
    public DateOnly? TrialEndDate { get; set; }

    /// <summary>Platform admin notes (e.g. "Paid via bank transfer 2026-09-01").</summary>
    [MaxLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigations
    public Organization? Organization { get; set; }
    public SubscriptionPlan? Plan { get; set; }
}
