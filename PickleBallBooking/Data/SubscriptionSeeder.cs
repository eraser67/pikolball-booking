using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Models;

namespace PickleBallBooking.Data;

/// <summary>
/// Phase 26: seeds the default subscription plans (Free, Basic, Pro) if none exist,
/// and assigns any organization that has no subscription row to the Free plan.
/// Safe to run on every startup — idempotent.
/// </summary>
public static class SubscriptionSeeder
{
    private const string FreePlanName  = "Free";
    private const string BasicPlanName = "Basic";
    private const string ProPlanName   = "Pro";

    public static async Task SeedAsync(WebApplication app)
    {
        using var scope  = app.Services.CreateScope();
        var context      = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger       = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                               .CreateLogger(nameof(SubscriptionSeeder));

        var now = DateTime.UtcNow;

        // ── 1. Seed default plans if they don't exist ─────────────────────────
        if (!await context.SubscriptionPlans.AnyAsync())
        {
            logger.LogInformation("Seeding default subscription plans...");

            context.SubscriptionPlans.AddRange(
                new SubscriptionPlan
                {
                    Name                = FreePlanName,
                    Description         = "Forever free — ideal for getting started.",
                    Price               = 0m,
                    BillingPeriod       = BillingPeriod.Monthly,
                    MaxCourts           = 2,
                    MaxBookingsPerMonth = null,
                    Features            = "Up to 2 courts\nEmail notifications\nGCash payments\nBooking management",
                    IsActive            = true,
                    IsFree              = true,
                    CreatedAt           = now,
                    UpdatedAt           = now
                },
                new SubscriptionPlan
                {
                    Name                = BasicPlanName,
                    Description         = "Great for small venues.",
                    Price               = 999m,
                    BillingPeriod       = BillingPeriod.Monthly,
                    MaxCourts           = 5,
                    MaxBookingsPerMonth = null,
                    Features            = "Up to 5 courts\nEmail notifications\nGCash payments\nBooking management\nPayment proof uploads",
                    IsActive            = true,
                    IsFree              = false,
                    CreatedAt           = now,
                    UpdatedAt           = now
                },
                new SubscriptionPlan
                {
                    Name                = ProPlanName,
                    Description         = "For large venues with multiple courts.",
                    Price               = 2499m,
                    BillingPeriod       = BillingPeriod.Monthly,
                    MaxCourts           = null,
                    MaxBookingsPerMonth = null,
                    Features            = "Unlimited courts\nEmail notifications\nGCash payments\nBooking management\nPayment proof uploads\nPriority support",
                    IsActive            = true,
                    IsFree              = false,
                    CreatedAt           = now,
                    UpdatedAt           = now
                });

            await context.SaveChangesAsync();
            logger.LogInformation("Default subscription plans seeded.");
        }

        // ── 2. Assign any org without a subscription to the Free plan ──────────
        var freePlan = await context.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.Name == FreePlanName);

        if (freePlan is null)
        {
            logger.LogWarning("Free plan not found — skipping org subscription assignment.");
            return;
        }

        var subscribedOrgIds = await context.Subscriptions
            .Select(s => s.OrganizationId)
            .ToHashSetAsync();

        var unsubscribedOrgs = await context.Organizations
            .Where(o => !subscribedOrgIds.Contains(o.Id))
            .ToListAsync();

        if (unsubscribedOrgs.Count > 0)
        {
            logger.LogInformation(
                "Assigning Free plan to {Count} organization(s) that had no subscription.",
                unsubscribedOrgs.Count);

            foreach (var org in unsubscribedOrgs)
            {
                context.Subscriptions.Add(new Subscription
                {
                    OrganizationId = org.Id,
                    PlanId         = freePlan.Id,
                    Status         = SubscriptionStatus.Trial,
                    TrialEndDate   = DateOnly.FromDateTime(now.AddDays(30)),
                    Notes          = "Auto-assigned on first startup (Phase 26 seed).",
                    CreatedAt      = now,
                    UpdatedAt      = now
                });
            }

            await context.SaveChangesAsync();
            logger.LogInformation("Subscription assignment complete.");
        }
    }
}
