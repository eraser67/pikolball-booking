using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Models;

namespace PickleBallBooking.Data;

/// <summary>
/// Single source of truth for the first ("Pikolball") organization used by the
/// multi-tenant foundation.
///
/// IMPORTANT - SCOPE:
/// This is NOT tenant context, tenant resolution, or tenant filtering. It is a
/// deliberately simple, single-tenant fallback: the application currently operates
/// as the original Pikolball tenant, so every tenant-owned row it writes must be
/// attached to that one organization to satisfy the Phase 20 NOT NULL foreign keys.
///
/// Per-request tenant resolution (subdomain/hostname, current-organization service,
/// global query filters) is intentionally NOT implemented here and belongs to a
/// later phase.
/// </summary>
public static class OrganizationDefaults
{
    /// <summary>The stable slug of the first organization. Used as the lookup key.</summary>
    public const string PikolballSlug = "pikolball";

    /// <summary>The display name of the first organization.</summary>
    public const string PikolballName = "Pikolball";

    /// <summary>
    /// Resolves the Pikolball organization id, creating the organization on first
    /// use if it does not yet exist. Resolution is by unique Slug (never by a
    /// hard-coded id) so it matches the Phase 20.4 migration's seed and constants.
    /// </summary>
    public static async Task<int> GetPikolballOrganizationIdAsync(ApplicationDbContext context, CancellationToken cancellationToken = default)
    {
        var existing = await context.Organizations
            .Where(o => o.Slug == PikolballSlug)
            .Select(o => o.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing != 0)
        {
            return existing;
        }

        var organization = new Organization
        {
            Name = PikolballName,
            Slug = PikolballSlug,
            Status = OrganizationStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.Organizations.Add(organization);
        await context.SaveChangesAsync(cancellationToken);

        return organization.Id;
    }
}
