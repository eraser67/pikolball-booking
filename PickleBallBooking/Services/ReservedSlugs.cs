namespace PickleBallBooking.Services;

/// <summary>
/// Phase 23: centralized reserved-slug policy for tenant organizations.
///
/// A reserved slug would collide with the platform itself (the apex/base domain, the
/// <c>www</c> host, common infrastructure hostnames, etc.), so it can never be
/// assigned to a tenant. This is the SINGLE source of truth - pages/services must
/// call <see cref="IsReserved"/> rather than scattering hard-coded checks.
/// </summary>
public interface IReservedSlugs
{
    /// <summary>True when <paramref name="slug"/> is reserved and cannot be a tenant.</summary>
    bool IsReserved(string? slug);
}

/// <inheritdoc />
public sealed class ReservedSlugs : IReservedSlugs
{
    /// <summary>
    /// Slugs that would conflict with the platform/application infrastructure. Kept
    /// intentionally broad and lower-cased; comparison is case-insensitive.
    /// </summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "www",
        "admin",
        "api",
        "app",
        "mail",
        "support",
        // Additional infrastructure/system hostnames that must never be tenants.
        "static",
        "cdn",
        "assets",
        "ftp",
        "ns",
        "ns1",
        "ns2",
        "smtp",
        "imap",
        "pop",
        "webmail",
        "dashboard",
        "portal",
        "account",
        "accounts",
        "auth",
        "login",
        "signup",
        "status",
        "docs",
        "help",
        "billing",
        "payments",
        "punitbola"
    };

    public bool IsReserved(string? slug)
        => !string.IsNullOrWhiteSpace(slug) && Reserved.Contains(slug.Trim());
}
