using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace PickleBallBooking.Services;

/// <summary>
/// ITelegramService implementation that dispatches messages via the Telegram Bot API.
/// https://core.telegram.org/bots/api#sendmessage
///
/// SECURITY: The BotToken is read from IOptions (User Secrets / env vars).
///           It is NEVER logged, exposed in errors, or sent to the client.
///
/// RELIABILITY: All exceptions are caught and logged. This service never throws,
///              making it safe to call fire-and-forget from BookingTelegramService
///              without delaying the booking or payment HTTP response.
/// </summary>
public sealed class TelegramService : ITelegramService
{
    private readonly HttpClient _http;
    private readonly TelegramOptions _opts;
    private readonly ILogger<TelegramService> _logger;

    public TelegramService(
        HttpClient http,
        IOptions<TelegramOptions> opts,
        ILogger<TelegramService> logger)
    {
        _http   = http;
        _opts   = opts.Value;
        _logger = logger;
    }

    public async Task<bool> SendMessageAsync(string chatId, string message, CancellationToken ct = default)
    {
        if (!_opts.Enabled)
        {
            _logger.LogDebug(
                "Telegram notifications disabled (Telegram:Enabled = false). Skipping message to chat {ChatId}",
                MaskChatId(chatId));
            return false;
        }

        if (string.IsNullOrWhiteSpace(_opts.BotToken))
        {
            _logger.LogWarning(
                "Telegram is enabled but BotToken is not configured. Skipping message to chat {ChatId}.",
                MaskChatId(chatId));
            return false;
        }

        if (string.IsNullOrWhiteSpace(chatId))
        {
            _logger.LogWarning("Telegram chatId is empty. Skipping message.");
            return false;
        }

        try
        {
            var endpoint = $"https://api.telegram.org/bot{_opts.BotToken}/sendMessage";
            var payload = new
            {
                chat_id = chatId,
                text = message,
                parse_mode = "Markdown"
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await _http.PostAsync(endpoint, jsonContent, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Telegram alert successfully sent to chat {ChatId}", MaskChatId(chatId));
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Telegram API returned status {StatusCode} for chat {ChatId}. Response: {Response}",
                (int)response.StatusCode,
                MaskChatId(chatId),
                errorBody);

            return false;
        }
        catch (Exception ex)
        {
            // Never rethrow — Telegram notification failures must never block bookings/payments
            _logger.LogError(ex, "Failed to send Telegram message to chat {ChatId}", MaskChatId(chatId));
            return false;
        }
    }

    private static string MaskChatId(string? chatId)
    {
        if (string.IsNullOrWhiteSpace(chatId)) return "[blank]";
        if (chatId.Length <= 4) return "****";
        return string.Concat(chatId.AsSpan(0, 3), "****", chatId.AsSpan(Math.Max(0, chatId.Length - 2)));
    }
}
