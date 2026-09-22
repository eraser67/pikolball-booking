namespace PickleBallBooking.Services;

/// <summary>
/// Phase 25: platform-level email delivery abstraction.
///
/// Implementations must be fire-and-forget safe: they catch their own exceptions
/// and log them rather than propagating, so a slow or unavailable SMTP server
/// never delays the booking or payment response to the customer.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends a single email. Never throws — logs on failure.
    /// </summary>
    Task SendAsync(
        string toAddress,
        string toName,
        string subject,
        string htmlBody,
        string? fromAddress = null,
        string? fromName = null,
        string? replyTo = null,
        CancellationToken ct = default);
}
