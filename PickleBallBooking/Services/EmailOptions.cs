namespace PickleBallBooking.Services;

/// <summary>
/// Email provider configuration, bound from the "Email" appsettings section.
/// Store the API key in User Secrets (dev) or environment variables (prod).
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Resend API key (starts with "re_").
    /// Never commit to source control — store in User Secrets or an env var.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>The "From" email address shown to recipients. Must be on a Resend-verified domain.</summary>
    public string FromAddress { get; init; } = string.Empty;

    /// <summary>The "From" display name shown to recipients.</summary>
    public string FromName { get; init; } = "Pikolball Booking";

    /// <summary>
    /// Set to false to skip sending emails (useful during development
    /// when the API key is not yet configured).
    /// </summary>
    public bool Enabled { get; init; } = false;
}
