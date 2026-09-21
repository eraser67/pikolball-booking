namespace PickleBallBooking.Services;

/// <summary>
/// Phase 21: request-scoped tenant context.
///
/// Exposes the organization that the current request is acting as. This is the
/// single server-side source of truth for tenant isolation: services and the EF
/// query filters must read the current organization from here and must NEVER
/// accept an organization id from query strings, form data, route values, or any
/// other client-controlled input.
///
/// <see cref="OrganizationId"/> is <c>null</c> when no organization could be
/// resolved for the request (for example, an authenticated user with no
/// organization membership). When it is <c>null</c>, tenant-owned data is
/// invisible: query filters match nothing and tenant-scoped writes are rejected.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// The current organization id, or <c>null</c> when no organization has been
    /// resolved for the request.
    /// </summary>
    int? OrganizationId { get; }

    /// <summary>
    /// True when an organization has been resolved for the current request.
    /// </summary>
    bool IsResolved { get; }
}

/// <summary>
/// Mutable request-scoped implementation of <see cref="ITenantContext"/>.
///
/// The value is populated exactly once per request by tenant-resolution
/// middleware (see <c>TenantResolutionMiddleware</c>). Being a mutable service
/// also lets tests construct a context bound to a specific organization without
/// standing up the whole HTTP pipeline.
///
/// This type is intentionally request-scoped so that concurrent requests never
/// share an organization id.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    /// <summary>
    /// The current organization id. Settable only so that resolution middleware
    /// (which has no other way to reach the scoped instance) and tests can bind it.
    /// Application code must treat this as read-only.
    /// </summary>
    public int? OrganizationId { get; set; }

    public bool IsResolved => OrganizationId.HasValue;
}
