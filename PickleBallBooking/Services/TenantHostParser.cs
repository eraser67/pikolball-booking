using System.Diagnostics.CodeAnalysis;

namespace PickleBallBooking.Services;

/// <summary>
/// Phase 22: pure hostname -> tenant slug parsing.
///
/// This type contains NO data access and NO client input other than the request
/// Host header, which is validated strictly against the configured base domain.
/// It exists so the security-critical parsing rules can be unit-tested in isolation:
///
///  - Only a STRICT single-label subdomain of the configured base domain yields a
///    slug. The base domain itself, <c>www.</c>, nested labels (<c>foo.bar.</c>),
///    other domains, empty hosts and malformed hosts all yield no slug.
///  - Matching is case-insensitive and ignores the port.
///  - A slug is only ever the left-most DNS label before the base domain; it is
///    never taken from the query string, route, form, cookie or request body.
///
/// NOTE: parsing NEVER falls back to a "default" tenant. When the host does not
/// clearly identify exactly one tenant, this returns false and the caller must
/// treat the request as having no tenant.
/// </summary>
public interface ITenantHostParser
{
    /// <summary>
    /// Attempts to extract the tenant slug from <paramref name="host"/> given the
    /// configured <paramref name="baseDomain"/>.
    /// </summary>
    /// <param name="host">
    /// The request host (may include a port, may be mixed-case). Only the host of the
    /// incoming request is trusted; callers must not pass client-declared values from
    /// query/route/form.
    /// </param>
    /// <param name="baseDomain">The configured base/apex domain.</param>
    /// <param name="slug">
    /// The extracted, normalized (lower-case) tenant slug when this method returns true.
    /// </param>
    /// <returns>
    /// True only when <paramref name="host"/> is a strict single-label subdomain of
    /// <paramref name="baseDomain"/>. False otherwise.
    /// </returns>
    bool TryGetTenantSlug(string? host, string? baseDomain, [NotNullWhen(true)] out string? slug);
}

/// <inheritdoc />
public sealed class TenantHostParser : ITenantHostParser
{
    public bool TryGetTenantSlug(string? host, string? baseDomain, [NotNullWhen(true)] out string? slug)
    {
        slug = null;

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(baseDomain))
        {
            return false;
        }

        var normalizedHost = NormalizeHost(host);
        var normalizedBase = NormalizeHost(baseDomain);

        if (normalizedHost.Length == 0 || normalizedBase.Length == 0)
        {
            return false;
        }

        // The host must END with ".<baseDomain>" (a dot separator is required so that
        // "evilpunitbola.com" or "notpunitbola.com" can never match "punitbola.com").
        var suffix = "." + normalizedBase;
        if (normalizedHost.Length <= suffix.Length || !normalizedHost.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        // The tenant label is everything before ".<baseDomain>".
        var label = normalizedHost[..^suffix.Length];

        // Reject the base domain itself and "www" (which are not tenants), nested
        // subdomains ("foo.bar") and anything with an empty label.
        if (label.Length == 0 || label.Contains('.'))
        {
            return false;
        }

        if (string.Equals(label, "www", StringComparison.Ordinal))
        {
            return false;
        }

        // The slug must be a single, syntactically valid DNS label (letters, digits and
        // hyphens; not starting or ending with a hyphen). This rejects hosts with
        // unexpected characters or underscores that a client might supply.
        if (!IsValidDnsLabel(label))
        {
            return false;
        }

        slug = label;
        return true;
    }

    /// <summary>
    /// Removes a port (if any), trims whitespace, drops a single trailing dot (FQDN
    /// root) and lower-cases the value. Case-insensitivity and port-insensitivity are
    /// both required by the Phase 22 hostname rules.
    /// </summary>
    private static string NormalizeHost(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        // Strip the port. IPv6 literal hosts are bracketed ("[::1]:5000"); after
        // removing the port they still contain a colon and will be rejected below.
        var colonIndex = trimmed.IndexOf(':');
        if (colonIndex >= 0)
        {
            trimmed = trimmed[..colonIndex];
        }

        // Drop a single trailing dot (e.g. "pikolball.punitbola.com.").
        if (trimmed.EndsWith(".", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^1];
        }

        return trimmed.ToLowerInvariant();
    }

    private static bool IsValidDnsLabel(string label)
    {
        if (label.Length is 0 or > 63)
        {
            return false;
        }

        if (label[0] == '-' || label[^1] == '-')
        {
            return false;
        }

        foreach (var c in label)
        {
            var isAlphaNumeric = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
            if (!isAlphaNumeric && c != '-')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Phase 23: exposes the Phase 22 hostname-label rules for reuse so tenant slug
    /// validation (at organization-creation time) uses exactly the same rules that
    /// hostname resolution enforces. A valid slug is a single, lower-case DNS label:
    /// letters/digits/hyphens, 1-63 chars, not starting or ending with a hyphen.
    /// </summary>
    public static bool IsValidSlugLabel(string? label)
        => !string.IsNullOrEmpty(label) && IsValidDnsLabel(label);
}
