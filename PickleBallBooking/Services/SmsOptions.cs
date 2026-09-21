namespace PickleBallBooking.Services;

/// <summary>
/// SMS provider configuration, bound from the "Sms" appsettings section.
/// Uses Semaphore (semaphore.co) for SMS dispatch in the Philippines.
/// </summary>
public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    /// <summary>
    /// Semaphore API key.
    /// Never commit real keys to source control — configure via User Secrets or environment variables (Sms__ApiKey).
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Sender name shown to recipients.
    /// Defaults to "SEMAPHORE" until an account has an approved sender name.
    /// </summary>
    public string SenderName { get; init; } = "SEMAPHORE";

    /// <summary>
    /// Base URL for Semaphore messages endpoint.
    /// </summary>
    public string ApiUrl { get; init; } = "https://api.semaphore.co/api/v4/messages";

    /// <summary>
    /// Set to false to disable SMS notifications (e.g. during local testing or when credits are not available).
    /// </summary>
    public bool Enabled { get; init; }
}
