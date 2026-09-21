using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PickleBallBooking.Services;

/// <summary>
/// Phase 23: authorization for tenant ("organization") administration pages.
///
/// A tenant admin page requires an authenticated user whose request resolved to an
/// organization via the HOSTNAME. Because the Phase 22 resolver only resolves a
/// tenant for an authenticated user who is a MEMBER of that organization, satisfying
/// this requirement is exactly equivalent to "this user administers the organization
/// identified by the hostname" - the OrganizationId is never taken from client input.
/// </summary>
public sealed class TenantResolvedRequirement : IAuthorizationRequirement
{
}

/// <summary>
/// Succeeds only when <see cref="ITenantContext.IsResolved"/> is true for the request.
/// </summary>
public sealed class TenantResolvedHandler : AuthorizationHandler<TenantResolvedRequirement>
{
    private readonly ITenantContext _tenantContext;

    public TenantResolvedHandler(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantResolvedRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        // PlatformAdmin can access tenant admin pages without a resolved subdomain.
        // They typically visit localhost directly to access the platform dashboard.
        if (context.User.IsInRole(PlatformRoles.PlatformAdmin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Regular org admins: the tenant must be resolved from the hostname.
        if (_tenantContext.IsResolved)
        {
            context.Succeed(requirement);
        }

        // Otherwise the requirement is not met and the request is challenged/denied
        // by the cookie handler (redirect to login or access denied).
        return Task.CompletedTask;
    }
}

/// <summary>
/// Phase 23: adds the tenant-admin policy and its handler to the service collection.
/// Kept in one place so the policy name and its requirement never drift apart.
/// </summary>
public static class TenantAdminAuthorization
{
    /// <summary>Policy requiring an authenticated member of the hostname-resolved tenant.</summary>
    public const string TenantAdminPolicy = "TenantAdmin";

    public static void AddTenantAdminPolicy(this AuthorizationOptions options)
    {
        options.AddPolicy(TenantAdminPolicy, policy =>
           policy.RequireAuthenticatedUser().AddRequirements(new TenantResolvedRequirement()));
    }

    public static IServiceCollection AddTenantAdminAuthorization(this IServiceCollection services)
    {
        // Scoped (not Singleton) because TenantResolvedHandler consumes the request-scoped
        // ITenantContext. Using Singleton would cause a captive dependency error.
        services.AddScoped<IAuthorizationHandler, TenantResolvedHandler>();
        return services;
    }
}
