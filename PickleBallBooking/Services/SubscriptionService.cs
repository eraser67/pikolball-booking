using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

// ─────────────────────────────────────────────────────────────────────────────
// Interface
// ─────────────────────────────────────────────────────────────────────────────

public interface ISubscriptionService
{
    // ── Plan management (platform admin) ──────────────────────────────────────

    /// <summary>All subscription plans, newest first.</summary>
    Task<List<SubscriptionPlan>> GetAllPlansAsync(CancellationToken ct = default);

    /// <summary>All active/selectable plans (for assign dropdowns).</summary>
    Task<List<SubscriptionPlan>> GetActivePlansAsync(CancellationToken ct = default);

    /// <summary>A single plan by Id, or null.</summary>
    Task<SubscriptionPlan?> GetPlanByIdAsync(int planId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new subscription plan.
    /// Returns false if a plan with the same name already exists.
    /// </summary>
    Task<bool> CreatePlanAsync(
        string name,
        string? description,
        decimal price,
        BillingPeriod billingPeriod,
        int? maxCourts,
        int? maxBookingsPerMonth,
        string? features,
        bool isActive,
        bool isFree,
        CancellationToken ct = default);

    /// <summary>Updates an existing subscription plan. Returns false if not found.</summary>
    Task<bool> UpdatePlanAsync(
        int planId,
        string name,
        string? description,
        decimal price,
        BillingPeriod billingPeriod,
        int? maxCourts,
        int? maxBookingsPerMonth,
        string? features,
        bool isActive,
        bool isFree,
        CancellationToken ct = default);

    // ── Subscription management (platform admin) ──────────────────────────────

    /// <summary>All subscriptions with org + plan details (platform view).</summary>
    Task<List<SubscriptionSummary>> GetAllSubscriptionsAsync(CancellationToken ct = default);

    /// <summary>Subscription for a specific organization, or null.</summary>
    Task<Subscription?> GetForOrganizationAsync(int organizationId, CancellationToken ct = default);

    /// <summary>
    /// Assigns or updates a subscription for an organization.
    /// Creates the row if it does not exist (upsert).
    /// </summary>
    Task<bool> AssignPlanAsync(
        int organizationId,
        int planId,
        SubscriptionStatus status,
        DateOnly? startDate,
        DateOnly? endDate,
        DateOnly? trialEndDate,
        string? notes,
        CancellationToken ct = default);

    /// <summary>Updates only the status of a subscription. Returns false if not found.</summary>
    Task<bool> UpdateStatusAsync(int subscriptionId, SubscriptionStatus status, CancellationToken ct = default);

    // ── Tenant-facing ─────────────────────────────────────────────────────────

    /// <summary>
    /// Current tenant's subscription (resolved from ITenantContext).
    /// Includes the plan navigation.
    /// </summary>
    Task<Subscription?> GetCurrentAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns true when the current tenant is allowed to accept new bookings
    /// based on their subscription status. Free plan orgs are always allowed.
    /// Blocked statuses: Expired, Suspended, Cancelled.
    /// </summary>
    Task<bool> CanAcceptBookingsAsync(CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────────────────────
// DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Platform-admin view of a subscription row.</summary>
public sealed record SubscriptionSummary(
    int SubscriptionId,
    int OrganizationId,
    string OrganizationName,
    string OrganizationSlug,
    int PlanId,
    string PlanName,
    SubscriptionStatus Status,
    DateOnly? StartDate,
    DateOnly? EndDate,
    DateOnly? TrialEndDate,
    DateTime UpdatedAt);

// ─────────────────────────────────────────────────────────────────────────────
// Implementation
// ─────────────────────────────────────────────────────────────────────────────

/// <inheritdoc />
public sealed class SubscriptionService : ISubscriptionService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;

    private static readonly HashSet<SubscriptionStatus> BlockedStatuses = new()
    {
        SubscriptionStatus.Expired,
        SubscriptionStatus.Suspended,
        SubscriptionStatus.Cancelled
    };

    public SubscriptionService(ApplicationDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    // ── Plans ─────────────────────────────────────────────────────────────────

    public async Task<List<SubscriptionPlan>> GetAllPlansAsync(CancellationToken ct = default)
        => await _context.SubscriptionPlans
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

    public async Task<List<SubscriptionPlan>> GetActivePlansAsync(CancellationToken ct = default)
        => await _context.SubscriptionPlans
            .Where(p => p.IsActive)
            .OrderBy(p => p.Price)
            .ToListAsync(ct);

    public async Task<SubscriptionPlan?> GetPlanByIdAsync(int planId, CancellationToken ct = default)
        => await _context.SubscriptionPlans.FindAsync([planId], ct);

    public async Task<bool> CreatePlanAsync(
        string name, string? description, decimal price, BillingPeriod billingPeriod,
        int? maxCourts, int? maxBookingsPerMonth, string? features,
        bool isActive, bool isFree, CancellationToken ct = default)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return false;

        var exists = await _context.SubscriptionPlans
            .AnyAsync(p => p.Name == trimmed, ct);
        if (exists) return false;

        var now = DateTime.UtcNow;
        _context.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Name                = trimmed,
            Description         = description?.Trim(),
            Price               = price,
            BillingPeriod       = billingPeriod,
            MaxCourts           = maxCourts,
            MaxBookingsPerMonth = maxBookingsPerMonth,
            Features            = features?.Trim(),
            IsActive            = isActive,
            IsFree              = isFree,
            CreatedAt           = now,
            UpdatedAt           = now
        });

        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UpdatePlanAsync(
        int planId, string name, string? description, decimal price, BillingPeriod billingPeriod,
        int? maxCourts, int? maxBookingsPerMonth, string? features,
        bool isActive, bool isFree, CancellationToken ct = default)
    {
        var plan = await _context.SubscriptionPlans.FindAsync([planId], ct);
        if (plan is null) return false;

        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return false;

        // Name uniqueness (excluding self).
        var nameConflict = await _context.SubscriptionPlans
            .AnyAsync(p => p.Name == trimmed && p.Id != planId, ct);
        if (nameConflict) return false;

        plan.Name                = trimmed;
        plan.Description         = description?.Trim();
        plan.Price               = price;
        plan.BillingPeriod       = billingPeriod;
        plan.MaxCourts           = maxCourts;
        plan.MaxBookingsPerMonth = maxBookingsPerMonth;
        plan.Features            = features?.Trim();
        plan.IsActive            = isActive;
        plan.IsFree              = isFree;
        plan.UpdatedAt           = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
        return true;
    }

    // ── Subscriptions ─────────────────────────────────────────────────────────

    public async Task<List<SubscriptionSummary>> GetAllSubscriptionsAsync(CancellationToken ct = default)
    {
        var rows = await _context.Subscriptions
            .Include(s => s.Organization)
            .Include(s => s.Plan)
            .OrderBy(s => s.Organization!.Name)
            .ToListAsync(ct);

        return rows.Select(s => new SubscriptionSummary(
            s.Id,
            s.OrganizationId,
            s.Organization?.Name ?? "—",
            s.Organization?.Slug ?? "—",
            s.PlanId,
            s.Plan?.Name ?? "—",
            s.Status,
            s.StartDate,
            s.EndDate,
            s.TrialEndDate,
            s.UpdatedAt))
            .ToList();
    }

    public async Task<Subscription?> GetForOrganizationAsync(int organizationId, CancellationToken ct = default)
        => await _context.Subscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, ct);

    public async Task<bool> AssignPlanAsync(
        int organizationId, int planId, SubscriptionStatus status,
        DateOnly? startDate, DateOnly? endDate, DateOnly? trialEndDate,
        string? notes, CancellationToken ct = default)
    {
        var existing = await _context.Subscriptions
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, ct);

        var now = DateTime.UtcNow;

        if (existing is null)
        {
            _context.Subscriptions.Add(new Subscription
            {
                OrganizationId = organizationId,
                PlanId         = planId,
                Status         = status,
                StartDate      = startDate,
                EndDate        = endDate,
                TrialEndDate   = trialEndDate,
                Notes          = notes,
                CreatedAt      = now,
                UpdatedAt      = now
            });
        }
        else
        {
            existing.PlanId       = planId;
            existing.Status       = status;
            existing.StartDate    = startDate;
            existing.EndDate      = endDate;
            existing.TrialEndDate = trialEndDate;
            existing.Notes        = notes;
            existing.UpdatedAt    = now;
        }

        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UpdateStatusAsync(int subscriptionId, SubscriptionStatus status, CancellationToken ct = default)
    {
        var sub = await _context.Subscriptions.FindAsync([subscriptionId], ct);
        if (sub is null) return false;

        sub.Status    = status;
        sub.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    // ── Tenant-facing ─────────────────────────────────────────────────────────

    public async Task<Subscription?> GetCurrentAsync(CancellationToken ct = default)
    {
        var id = _tenantContext.OrganizationId;
        if (id is null) return null;

        return await _context.Subscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.OrganizationId == id.Value, ct);
    }

    public async Task<bool> CanAcceptBookingsAsync(CancellationToken ct = default)
    {
        var id = _tenantContext.OrganizationId;
        if (id is null) return false;

        var sub = await _context.Subscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.OrganizationId == id.Value, ct);

        // No subscription row = block.
        if (sub is null) return false;

        // Suspended and Cancelled block ALL orgs — even Free plan.
        // These are explicit admin actions that override everything.
        if (sub.Status is SubscriptionStatus.Suspended or SubscriptionStatus.Cancelled)
            return false;

        // Free plan: Expired doesn't block them (no billing), but Suspended/Cancelled above still do.
        if (sub.Plan?.IsFree == true) return true;

        // Paid plans: block if Expired, Suspended, or Cancelled.
        return !BlockedStatuses.Contains(sub.Status);
    }
}
