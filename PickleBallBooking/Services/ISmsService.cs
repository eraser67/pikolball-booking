namespace PickleBallBooking.Services;

/// <summary>
/// Platform-level SMS delivery abstraction.
///
/// Implementations must be fire-and-forget safe: they catch their own exceptions
/// and log them rather than propagating, so external gateway latency or failures
/// never delay booking or payment processing.
/// </summary>
public interface ISmsService
{
    /// <summary>
    /// Sends a single SMS to the specified recipient phone number.
    /// Never throws — logs warnings/errors on failure and returns false.
    /// </summary>
    /// <param name="recipientPhoneNumber">The target mobile number (e.g. 0917xxxxxxx or +63917xxxxxxx).</param>
    /// <param name="message">The text message content to send.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>True if accepted by the gateway, false otherwise.</returns>
    Task<bool> SendSmsAsync(string recipientPhoneNumber, string message, CancellationToken ct = default);
}
