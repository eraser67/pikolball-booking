using Microsoft.Extensions.DependencyInjection;
using PickleBallBooking.Services;

namespace PickleBallBooking.Data;

/// <summary>
/// Phase 21/22: helpers for seeding/background work that runs OUTSIDE an HTTP request.
///
/// Seeders execute at startup (via a manually created service scope) where there is
/// no <c>HttpContext</c> and, therefore, no request hostname to resolve a tenant
/// from. Because the global query filters are driven by <see cref="ITenantContext"/>,
/// a seeder must first bind the scope's tenant context to an organization; otherwise
/// every tenant-owned query/write is (correctly) treated as having no tenant.
///
/// Background work targets the original ("Pikolball") organization explicitly. This
/// is NOT hostname resolution and is NOT a fallback for request traffic - request
/// tenants are always resolved from the hostname by <see cref="ITenantResolver"/>.
/// </summary>
public static class TenantScopeExtensions
{
    /// <summary>
    /// Resolves the tenant for the supplied scope and binds it to the scope's
    /// <see cref="TenantContext"/>. Returns the resolved organization id, or
    /// <c>null</c> if none could be resolved.
    ///
    /// Outside a request (no <see cref="HttpContext"/>) the original Pikolball
    /// organization is used, since background work is tied to the first tenant.
    /// </summary>
    public static async Task<int?> UseResolvedTenantAsync(
        this IServiceScope scope,
        CancellationToken cancellationToken = default)
    {
        var tenantContext = (TenantContext)scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();

        // Inside a request, resolve from the hostname (the single tenant selector).
        if (httpContextAccessor.HttpContext is not null)
        {
            var resolver = scope.ServiceProvider.GetRequiredService<ITenantResolver>();
            var requestOrganizationId = await resolver.ResolveOrganizationIdAsync(cancellationToken);
            tenantContext.OrganizationId = requestOrganizationId;
            return requestOrganizationId;
        }

        // No request: bind the original Pikolball tenant for startup/background work.
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var organizationId = await OrganizationDefaults.GetPikolballOrganizationIdAsync(context, cancellationToken);
        tenantContext.OrganizationId = organizationId;
        return organizationId;
    }

    /// <summary>
    /// Same as <see cref="UseResolvedTenantAsync"/> but throws when no organization
    /// could be resolved. Use this in seeders, where a tenant is mandatory.
    /// </summary>
    public static async Task<int> RequireResolvedTenantAsync(
        this IServiceScope scope,
        CancellationToken cancellationToken = default)
    {
        var organizationId = await scope.UseResolvedTenantAsync(cancellationToken);
        return organizationId
            ?? throw new InvalidOperationException(
                "A tenant must be resolvable before seeding tenant-owned data.");
    }
}

