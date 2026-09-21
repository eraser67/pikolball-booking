namespace PickleBallBooking.Services;

/// <summary>
/// Phase 25: SMTP email configuration, bound from the "Email" section.
/// Store credentials in User Secrets (dev) or environment variables (prod).
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>SMTP host, e.g. "smtp.gmail.com".</summary>
    public string SmtpHost { get; init; } = "smtp.gmail.com";

    /// <summary>SMTP port. 587 = STARTTLS, 465 = SSL.</summary>
    public int SmtpPort { get; init; } = 587;

    /// <summary>SMTP username (usually the full email address).</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// SMTP password or Gmail App Password.
    /// Never commit to source control — store in User Secrets or env var.
    /// </summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>The "From" email address shown to recipients.</summary>
    public string FromAddress { get; init; } = string.Empty;

    /// <summary>The "From" display name shown to recipients.</summary>
    public string FromName { get; init; } = "Pikolball Booking";

    /// <summary>
    /// Set to false to skip sending emails (useful during development
    /// when SMTP credentials are not yet configured).
    /// </summary>
    public bool Enabled { get; init; } = false;
}
