namespace PickleBallBooking.Services;

/// <summary>
/// Phase 22: strongly-typed configuration for hostname/subdomain tenant resolution.
///
/// Bound from the <c>Tenant</c> configuration section:
/// <code>
/// {
///   "Tenant": {
///     "BaseDomain": "punitbola.com"
///   }
/// }
/// </code>
///
/// The base domain is NEVER hard-coded in application logic. It is read from
/// configuration so local development and tests can supply their own value
/// (for example, via <c>Tenant__BaseDomain</c> environment variables or
/// <c>appsettings.Development.json</c>) without any production domain baked in.
/// </summary>
public sealed class TenantOptions
{
    /// <summary>
    /// The configuration section that this type is bound from.
    /// </summary>
    public const string SectionName = "Tenant";

    /// <summary>
    /// The apex/base domain that tenant subdomains hang off, e.g. <c>punitbola.com</c>.
    /// A request host only identifies a tenant when it is a strict, single-label
    /// subdomain of this base domain (e.g. <c>pikolball.punitbola.com</c>).
    ///
    /// May be empty/unset in environments where hostname resolution is not configured;
    /// in that case no hostname resolves to a tenant (fail closed).
    /// </summary>
    public string BaseDomain { get; set; } = string.Empty;
}
