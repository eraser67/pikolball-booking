using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

/// <summary>
/// Phase 21/22: resolves the current request's organization id from the request
/// hostname (subdomain), then applies membership authorization for authenticated
/// users.
///
/// Resolution rules (server-side only - never client input):
///  1. The tenant is identified by the request HOSTNAME mapping to an
///     <see cref="Organization.Slug"/>. The host must be a strict, single-label
///     subdomain of the configured <c>Tenant:BaseDomain</c> (e.g.
///     <c>pikolball.punitbola.com</c> -> slug <c>pikolball</c>). The base domain
///     itself, <c>www.</c>, nested subdomains, other domains, unknown slugs,
///     inactive organizations, malformed/missing hosts and requests whose host is
///     absent all resolve to NO tenant.
///  2. If an organization is resolved AND the user is authenticated, the user must
///     be a member of that organization. A member of Organization A visiting
///     Organization B's subdomain resolves to NO tenant (their membership in A does
///     NOT grant access to B).
///  3. Anonymous users may access a valid, active tenant through its hostname; if
///     the hostname does not identify a valid tenant there is NO fallback.
///
/// The hostname is the ONLY tenant selector. Query string, route values, form
/// fields, cookies, headers other than Host, hidden fields, request bodies and any
/// client-supplied OrganizationId are never consulted.
/// </summary>
public interface ITenantResolver
{
    Task<int?> ResolveOrganizationIdAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class TenantResolver : ITenantResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ApplicationDbContext _context;
    private readonly ITenantHostParser _hostParser;
    private readonly TenantOptions _options;

    public TenantResolver(
        IHttpContextAccessor httpContextAccessor,
        ApplicationDbContext context,
        ITenantHostParser hostParser,
        IOptions<TenantOptions> options)
    {
        _httpContextAccessor = httpContextAccessor;
        _context = context;
        _hostParser = hostParser;
        _options = options.Value;
    }

    public async Task<int?> ResolveOrganizationIdAsync(CancellationToken cancellationToken = default)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            // No request context (background/seeding scope): callers bind the tenant
            // explicitly via TenantScopeExtensions, so nothing is resolved here.
            return null;
        }

        // The selector is the request host ONLY. Never the query string, route, form,
        // cookie, body, or a client-supplied organization id.
        var host = httpContext.Request.Host.Host;
        if (!_hostParser.TryGetTenantSlug(host, _options.BaseDomain, out var slug))
        {
            // Unknown / missing / malformed / base-domain / www / nested host: no tenant.
            return null;
        }

        // Resolve the slug to an ACTIVE organization. Organizations is not tenant
        // filtered (it is the tenant table itself), so this lookup is intentional and
        // is keyed solely by the hostname-derived slug.
        var organization = await _context.Organizations
            .Where(o => o.Slug == slug && o.Status == OrganizationStatus.Active)
            .Select(o => new { o.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (organization is null)
        {
            // Unknown or inactive tenant: do NOT fall back to Pikolball.
            return null;
        }

        // If the caller is authenticated, they must be a member of the hostname-identified
        // organization. Membership in another organization does not grant access here.
        var user = httpContext.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return null;
            }

            var isMember = await _context.OrganizationMembers
                .AnyAsync(m => m.OrganizationId == organization.Id && m.UserId == userId, cancellationToken);

            if (!isMember)
            {
                // Authenticated but not a member of this tenant: no tenant resolved, so
                // tenant-owned data stays invisible and access is denied by the existing
                // authorization model (e.g. AuthorizeFolder("/Admin")).
                return null;
            }
        }

        // Anonymous users, and members of this organization, resolve to the tenant.
        return organization.Id;
    }
}
