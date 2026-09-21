namespace PickleBallBooking.Services;

/// <summary>
/// Platform-level Telegram message delivery abstraction.
///
/// Implementations must be fire-and-forget safe: they catch their own exceptions
/// and log them rather than propagating, ensuring Telegram API latency or failures
/// never disrupt bookings or payments.
/// </summary>
public interface ITelegramService
{
    /// <summary>
    /// Sends a Telegram message to the specified chat or group.
    /// Never throws — logs warnings/errors on failure and returns false.
    /// </summary>
    /// <param name="chatId">The destination Telegram user or group chat ID.</param>
    /// <param name="message">The text message (supports Markdown formatting).</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>True if accepted by Telegram API, false otherwise.</returns>
    Task<bool> SendMessageAsync(string chatId, string message, CancellationToken ct = default);
}
