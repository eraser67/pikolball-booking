using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

/// <summary>
/// Result of attempting to create an organization (with its initial owner).
/// </summary>
public sealed class CreateOrganizationResult
{
    public bool Success { get; init; }

    /// <summary>A single field-keyed validation message (e.g. "Slug" -> "...").</summary>
    public IReadOnlyDictionary<string, string> Errors { get; init; } = new Dictionary<string, string>();

    /// <summary>The created organization, when <see cref="Success"/> is true.</summary>
    public Organization? Organization { get; init; }

    /// <summary>
    /// True when a NEW Identity user was created for the owner (and therefore needs
    /// to activate their account / set a password). False when an existing user was
    /// reused.
    /// </summary>
    public bool OwnerAccountCreated { get; init; }

    /// <summary>
    /// The Identity user id of the owner (whether newly created or reused). Used by the
    /// caller to build the activation link for a newly created owner.
    /// </summary>
    public string? OwnerUserId { get; init; }

    /// <summary>
    /// A one-time account activation token for a newly created owner, or <c>null</c>
    /// when an existing user was reused. The caller (platform admin UI) turns this into
    /// an activation link. It is never a password and is not stored in plaintext.
    /// </summary>
    public string? OwnerActivationToken { get; init; }
}

/// <summary>
/// Organization/tenant administration operations (Phase 23).
///
/// IMPORTANT - tenancy:
///  - <see cref="Organization"/> and <see cref="OrganizationMember"/> are global (NOT
///    tenant-filtered), because a user must be able to read their own memberships and
///    the platform admin must manage all organizations.
///  - Platform-scoped operations (create org, list all, set status) are only invoked
///    from pages protected by the platform-admin authorization policy.
///  - Tenant-scoped operations (update name, list members) take the CURRENT tenant id
///    from <see cref="ITenantContext"/> - never from client input - so a tenant can
///    only ever act on the organization the hostname resolved to.
/// </summary>
public interface IOrganizationService
{
    /// <summary>
    /// Creates an organization and its initial owner as ONE logical operation:
    /// Organization -> OrganizationMember (OrganizationOwner) -> IdentityUser.
    ///
    /// If the owner email already belongs to an Identity user, that user is REUSED
    /// (no duplicate account) and only the new organization membership is added. Other
    /// memberships are left untouched.
    /// </summary>
    Task<CreateOrganizationResult> CreateAsync(
        string name,
        string slug,
        string ownerName,
        string ownerEmail,
        CancellationToken cancellationToken = default);

    /// <summary>All organizations (platform view) with their owner(s), newest first.</summary>
    Task<List<OrganizationSummary>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>A single organization with owners (platform view), or null.</summary>
    Task<OrganizationSummary?> GetByIdAsync(int organizationId, CancellationToken cancellationToken = default);

    /// <summary>Sets an organization's status (platform admin only).</summary>
    Task<bool> SetStatusAsync(int organizationId, OrganizationStatus status, CancellationToken cancellationToken = default);

    /// <summary>The current tenant organization (from <see cref="ITenantContext"/>), or null.</summary>
    Task<Organization?> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>Renames the CURRENT tenant organization. Slug/status are not editable here.</summary>
    Task<bool> RenameCurrentAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the CURRENT tenant organization's public location fields
    /// (address, latitude, longitude). Any field may be null to clear it.
    /// </summary>
    Task<bool> UpdateLocationAsync(
        string? address,
        double? latitude,
        double? longitude,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the notification email address for the CURRENT tenant organization.
    /// Null or empty clears the field (disabling org notifications).
    /// </summary>
    Task<bool> UpdateNotificationEmailAsync(string? email, CancellationToken cancellationToken = default);

    /// <summary>Members of the CURRENT tenant organization (with their Identity info).</summary>
    Task<List<OrganizationMemberView>> GetCurrentMembersAsync(CancellationToken cancellationToken = default);

    // ── Platform-scoped (cross-tenant) member management ────────────────────

    /// <summary>All members of any org by ID (platform admin only).</summary>
    Task<List<OrganizationMemberView>> GetMembersOfOrgAsync(int organizationId, CancellationToken ct = default);

    /// <summary>
    /// Adds a user to any org by email (platform admin only). If the user doesn't
    /// exist in Identity, returns (false, "User not found."). If already a member,
    /// returns (false, "Already a member.").
    /// </summary>
    Task<(bool Success, string? Error)> AddMemberToOrgAsync(int organizationId, string email, OrganizationRole role, CancellationToken ct = default);

    /// <summary>Removes a member from any org (platform admin only).</summary>
    Task<bool> RemoveMemberFromOrgAsync(int organizationId, string userId, CancellationToken ct = default);

    /// <summary>Changes a member's role in any org (platform admin only).</summary>
    Task<bool> UpdateMemberRoleInOrgAsync(int organizationId, string userId, OrganizationRole newRole, CancellationToken ct = default);
}

/// <summary>Platform view of an organization.</summary>
public sealed record OrganizationSummary(
    int Id,
    string Name,
    string Slug,
    OrganizationStatus Status,
    DateTime CreatedAt,
    IReadOnlyList<OrganizationOwnerView> Owners);

/// <summary>An owner/member of an organization joined with Identity account info.</summary>
public sealed record OrganizationOwnerView(
    string UserId,
    string? Email,
    OrganizationRole Role,
    DateTime CreatedAt);

/// <summary>A member row for the tenant "Users" page.</summary>
public sealed record OrganizationMemberView(
    string UserId,
    string? Email,
    OrganizationRole Role,
    DateTime CreatedAt);

/// <inheritdoc />
public sealed class OrganizationService : IOrganizationService
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ITenantContext _tenantContext;
    private readonly IReservedSlugs _reservedSlugs;

    public OrganizationService(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        ITenantContext tenantContext,
        IReservedSlugs reservedSlugs)
    {
        _context = context;
        _userManager = userManager;
        _tenantContext = tenantContext;
        _reservedSlugs = reservedSlugs;
    }

    public async Task<CreateOrganizationResult> CreateAsync(
        string name,
        string slug,
        string ownerName,
        string ownerEmail,
        CancellationToken cancellationToken = default)
    {
        // Normalize the same way hostname resolution and Identity do.
        var trimmedName = (name ?? string.Empty).Trim();
        var normalizedSlug = (slug ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedEmail = (ownerEmail ?? string.Empty).Trim();

        var errors = await ValidateAsync(trimmedName, normalizedSlug, normalizedEmail, cancellationToken);
        if (errors.Count > 0)
        {
            return new CreateOrganizationResult { Success = false, Errors = errors };
        }

        // Reuse an existing Identity user when the email is already registered; never
        // create a duplicate. Existing memberships are preserved.
        var owner = await _userManager.FindByEmailAsync(normalizedEmail);
        var ownerAccountCreated = false;
        string? activationToken = null;

        if (owner is null)
        {
            owner = new IdentityUser
            {
                UserName = normalizedEmail,
                Email = normalizedEmail,
                // Not confirmed: the owner must activate and set their own password.
                EmailConfirmed = false
            };

            // No password is set here - the owner establishes credentials through the
            // activation flow. This keeps a permanent password out of the admin UI.
            var createResult = await _userManager.CreateAsync(owner);
            if (!createResult.Succeeded)
            {
                return new CreateOrganizationResult
                {
                    Success = false,
                    Errors = new Dictionary<string, string>
                    {
                        ["OwnerEmail"] = string.Join(" ", createResult.Errors.Select(e => e.Description))
                    }
                };
            }

            ownerAccountCreated = true;
            activationToken = await _userManager.GenerateEmailConfirmationTokenAsync(owner);
        }

        var now = DateTime.UtcNow;
        var organization = new Organization
        {
            Name = trimmedName,
            Slug = normalizedSlug,
            Status = OrganizationStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Organization + its initial owner membership are ONE logical operation.
        // Npgsql's NpgsqlRetryingExecutionStrategy (EnableRetryOnFailure) forbids
        // user-initiated transactions unless they run inside CreateExecutionStrategy().
        var strategy = _context.Database.CreateExecutionStrategy();
        var transactionResult = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                _context.Organizations.Add(organization);
                await _context.SaveChangesAsync(cancellationToken);

                _context.OrganizationMembers.Add(new OrganizationMember
                {
                    OrganizationId = organization.Id,
                    UserId = owner.Id,
                    Role = OrganizationRole.OrganizationOwner,
                    CreatedAt = now
                });

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException)
            {
                // Safety net for a concurrent insert of the same slug.
                await transaction.RollbackAsync(cancellationToken);
                _context.ChangeTracker.Clear();
                return false;
            }
        });

        if (!transactionResult)
        {
            return new CreateOrganizationResult
            {
                Success = false,
                Errors = new Dictionary<string, string>
                {
                    ["Slug"] = "That subdomain is already taken."
                }
            };
        }

        return new CreateOrganizationResult
        {
            Success = true,
            Organization = organization,
            OwnerUserId = owner.Id,
            OwnerAccountCreated = ownerAccountCreated,
            OwnerActivationToken = activationToken
        };
    }

    private async Task<Dictionary<string, string>> ValidateAsync(
        string name,
        string slug,
        string email,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();

        // Organization name
        if (string.IsNullOrWhiteSpace(name))
        {
            errors["Name"] = "Organization name is required.";
        }
        else if (name.Length > 150)
        {
            errors["Name"] = "Organization name must be 150 characters or fewer.";
        }

        // Slug: same rules as hostname resolution (Phase 22).
        if (string.IsNullOrWhiteSpace(slug))
        {
            errors["Slug"] = "Subdomain is required.";
        }
        else if (slug.Length > 63 || !TenantHostParser.IsValidSlugLabel(slug))
        {
            errors["Slug"] = "Subdomain must be a single DNS label (letters, numbers, hyphens; no dots, spaces or symbols).";
        }
        else if (_reservedSlugs.IsReserved(slug))
        {
            errors["Slug"] = "That subdomain is reserved and cannot be used.";
        }
        else if (await _context.Organizations.AnyAsync(o => o.Slug == slug, cancellationToken))
        {
            errors["Slug"] = "That subdomain is already taken.";
        }

        // Owner email: required + a valid format (matches Identity expectations).
        if (string.IsNullOrWhiteSpace(email))
        {
            errors["OwnerEmail"] = "Owner email is required.";
        }
        else if (!IsValidEmail(email))
        {
            errors["OwnerEmail"] = "Enter a valid email address.";
        }

        return errors;
    }

    /// <summary>
    /// Lightweight email-format check mirroring ASP.NET Core Identity's expectations
    /// (a non-empty local part, an "@", and a domain containing a dot). We avoid adding
    /// a new dependency; the attribute-level check in the page provides the same UX.
    /// </summary>
    private static bool IsValidEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0 || at != email.LastIndexOf('@') || at == email.Length - 1)
        {
            return false;
        }

        var domain = email[(at + 1)..];
        if (domain.Length == 0 || domain.Contains(' ') || email.Contains(' '))
        {
            return false;
        }

        var dot = domain.IndexOf('.');
        return dot > 0 && dot < domain.Length - 1;
    }

    public async Task<List<OrganizationSummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var organizations = await _context.Organizations
            .AsNoTracking()
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        return await BuildSummariesAsync(organizations, cancellationToken);
    }

    public async Task<OrganizationSummary?> GetByIdAsync(int organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await _context.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);

        if (organization is null)
        {
            return null;
        }

        var summaries = await BuildSummariesAsync(new List<Organization> { organization }, cancellationToken);
        return summaries.Single();
    }

    private async Task<List<OrganizationSummary>> BuildSummariesAsync(
        List<Organization> organizations,
        CancellationToken cancellationToken)
    {
        if (organizations.Count == 0)
        {
            return new List<OrganizationSummary>();
        }

        var ids = organizations.Select(o => o.Id).ToList();

        var memberships = await _context.OrganizationMembers
            .AsNoTracking()
            .Where(m => ids.Contains(m.OrganizationId))
            .ToListAsync(cancellationToken);

        var userIds = memberships.Select(m => m.UserId).Distinct().ToList();
        var users = await _context.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);

        return organizations
            .Select(o => new OrganizationSummary(
                o.Id,
                o.Name,
                o.Slug,
                o.Status,
                o.CreatedAt,
                memberships
                    .Where(m => m.OrganizationId == o.Id)
                    .OrderBy(m => m.Role)
                    .ThenBy(m => m.CreatedAt)
                    .Select(m => new OrganizationOwnerView(
                        m.UserId,
                        users.TryGetValue(m.UserId, out var email) ? email : null,
                        m.Role,
                        m.CreatedAt))
                    .ToList()))
            .ToList();
    }

    public async Task<bool> SetStatusAsync(
        int organizationId,
        OrganizationStatus status,
        CancellationToken cancellationToken = default)
    {
        var organization = await _context.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);

        if (organization is null)
        {
            return false;
        }

        organization.Status = status;
        organization.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Organization?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        // The tenant comes from ITenantContext (hostname-resolved), never client input.
        var id = _tenantContext.OrganizationId;
        if (id is null)
        {
            return null;
        }

        return await _context.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id.Value, cancellationToken);
    }

    public async Task<bool> RenameCurrentAsync(string name, CancellationToken cancellationToken = default)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 150)
        {
            return false;
        }

        var id = _tenantContext.OrganizationId;
        if (id is null)
        {
            return false;
        }

        var organization = await _context.Organizations
            .FirstOrDefaultAsync(o => o.Id == id.Value, cancellationToken);

        if (organization is null)
        {
            return false;
        }

        organization.Name = trimmed;
        organization.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateLocationAsync(
        string? address,
        double? latitude,
        double? longitude,
        CancellationToken cancellationToken = default)
    {
        var id = _tenantContext.OrganizationId;
        if (id is null) return false;

        var organization = await _context.Organizations
            .FirstOrDefaultAsync(o => o.Id == id.Value, cancellationToken);

        if (organization is null) return false;

        organization.Address   = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        organization.Latitude  = latitude;
        organization.Longitude = longitude;
        organization.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateNotificationEmailAsync(string? email, CancellationToken cancellationToken = default)
    {
        var id = _tenantContext.OrganizationId;
        if (id is null) return false;

        var organization = await _context.Organizations
            .FirstOrDefaultAsync(o => o.Id == id.Value, cancellationToken);

        if (organization is null) return false;

        var trimmed = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        organization.NotificationEmail = trimmed;
        organization.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<List<OrganizationMemberView>> GetCurrentMembersAsync(CancellationToken cancellationToken = default)
    {
        var id = _tenantContext.OrganizationId;
        if (id is null)
        {
            return new List<OrganizationMemberView>();
        }

        var memberships = await _context.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.OrganizationId == id.Value)
            .OrderBy(m => m.Role)
            .ThenBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        var userIds = memberships.Select(m => m.UserId).Distinct().ToList();
        var users = await _context.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);

        return memberships
            .Select(m => new OrganizationMemberView(
                m.UserId,
                users.TryGetValue(m.UserId, out var email) ? email : null,
                m.Role,
                m.CreatedAt))
            .ToList();
    }

    // ── Platform-scoped (cross-tenant) member management ────────────────────

    public async Task<List<OrganizationMemberView>> GetMembersOfOrgAsync(int organizationId, CancellationToken ct = default)
    {
        var memberships = await _context.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.OrganizationId == organizationId)
            .OrderBy(m => m.Role)
            .ThenBy(m => m.CreatedAt)
            .ToListAsync(ct);

        var userIds = memberships.Select(m => m.UserId).Distinct().ToList();
        var users = await _context.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email, ct);

        return memberships
            .Select(m => new OrganizationMemberView(
                m.UserId,
                users.TryGetValue(m.UserId, out var email) ? email : null,
                m.Role,
                m.CreatedAt))
            .ToList();
    }

    public async Task<(bool Success, string? Error)> AddMemberToOrgAsync(
        int organizationId, string email, OrganizationRole role, CancellationToken ct = default)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email.Trim(), ct);

        if (user is null)
            return (false, "No account found with that email address.");

        var existing = await _context.OrganizationMembers
            .AnyAsync(m => m.OrganizationId == organizationId && m.UserId == user.Id, ct);

        if (existing)
            return (false, "This user is already a member of the organization.");

        _context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organizationId,
            UserId         = user.Id,
            Role           = role,
            CreatedAt      = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<bool> RemoveMemberFromOrgAsync(int organizationId, string userId, CancellationToken ct = default)
    {
        var member = await _context.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId, ct);

        if (member is null) return false;

        _context.OrganizationMembers.Remove(member);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UpdateMemberRoleInOrgAsync(
        int organizationId, string userId, OrganizationRole newRole, CancellationToken ct = default)
    {
        var member = await _context.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId, ct);

        if (member is null) return false;

        member.Role = newRole;
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
