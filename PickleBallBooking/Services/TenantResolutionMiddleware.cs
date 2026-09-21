namespace PickleBallBooking.Services;

/// <summary>
/// Phase 21: populates the request-scoped <see cref="TenantContext"/> before any
/// page handler or service runs, so the DbContext's global query filters and write
/// guard always see the current organization.
///
/// This must be placed AFTER <c>UseAuthentication</c> (so <c>HttpContext.User</c>
/// is populated) and before the endpoint middleware.
/// </summary>
public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext, ITenantResolver resolver)
    {
        tenantContext.OrganizationId = await resolver.ResolveOrganizationIdAsync(context.RequestAborted);
        await _next(context);
    }
}
