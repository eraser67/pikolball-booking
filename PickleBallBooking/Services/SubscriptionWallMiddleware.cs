using PickleBallBooking.Services;

namespace PickleBallBooking.Services;

/// <summary>
/// Phase 26: subscription wall middleware.
///
/// For authenticated users with an active tenant context who are accessing
/// /Admin/* pages, checks whether the organization's subscription allows access.
/// If the subscription is Expired, Suspended, or Cancelled, redirects to the
/// /Admin/SubscriptionRequired wall page.
///
/// Exceptions (always allowed through):
/// - PlatformAdmin role: always bypassed (they manage subscriptions)
/// - /Admin/SubscriptionRequired itself (prevents redirect loop)
/// - /Admin/OrgSettings (org admin needs to see their info)
/// - /Admin/Bookings (existing bookings are read-only — data is not deleted)
/// - Static files and non-admin routes
/// </summary>
public class SubscriptionWallMiddleware
{
    private readonly RequestDelegate _next;

    // Pages that are always accessible regardless of subscription status.
    private static readonly HashSet<string> AllowedAdminPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/Admin/SubscriptionRequired",
        "/Admin/OrgSettings",
        "/Admin/Bookings",
        "/Admin/Bookings/Index",
    };

    public SubscriptionWallMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ISubscriptionService subscriptionService, ITenantContext tenantContext, ILoggerFactory loggerFactory)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Only intercept authenticated /Admin/* requests.
        if (context.User.Identity?.IsAuthenticated == true
            && path.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase)
            && !IsAllowedPath(path))
        {
            var logger = loggerFactory.CreateLogger<SubscriptionWallMiddleware>();

            // Platform admins are never blocked — they manage subscriptions.
            if (context.User.IsInRole(PlatformRoles.PlatformAdmin))
            {
                logger.LogDebug("[SubscriptionWall] Bypassed: user is PlatformAdmin.");
            }
            else if (!tenantContext.OrganizationId.HasValue)
            {
                logger.LogDebug("[SubscriptionWall] Bypassed: no tenant resolved for this request.");
            }
            else
            {
                var canBook = await subscriptionService.CanAcceptBookingsAsync();
                logger.LogDebug("[SubscriptionWall] OrgId={OrgId} CanAccept={CanAccept} Path={Path}",
                    tenantContext.OrganizationId, canBook, path);

                if (!canBook)
                {
                    context.Response.Redirect("/Admin/SubscriptionRequired");
                    return;
                }
            }
        }

        await _next(context);
    }

    private static bool IsAllowedPath(string path)
    {
        foreach (var allowed in AllowedAdminPaths)
        {
            if (path.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
