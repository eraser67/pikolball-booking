using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace PickleBallBooking.Services;

/// <summary>
/// ISmsService implementation that sends SMS via the Semaphore REST API (v4).
/// https://semaphore.co/docs
///
/// SECURITY: The API key is read from IOptions (User Secrets / env vars).
///           It is NEVER logged, exposed in errors, or sent to the client.
///
/// RELIABILITY: All exceptions are caught and logged. This service never throws,
///              making it safe to call fire-and-forget from BookingSmsService
///              without delaying the booking or payment HTTP response.
/// </summary>
public sealed class SemaphoreSmsService : ISmsService
{
    private readonly HttpClient _http;
    private readonly SmsOptions _opts;
    private readonly ILogger<SemaphoreSmsService> _logger;

    public SemaphoreSmsService(
        HttpClient http,
        IOptions<SmsOptions> opts,
        ILogger<SemaphoreSmsService> logger)
    {
        _http   = http;
        _opts   = opts.Value;
        _logger = logger;
    }

    /// <summary>
    /// Normalizes a Philippine mobile number into standard 11-digit format (09XXXXXXXXX).
    /// Accepts variations like: +639171234567, 639171234567, 0917-123-4567, 9171234567.
    /// Returns null if the number cannot be parsed into a valid 11-digit PH mobile number.
    /// </summary>
    public static string? NormalizePhoneNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // Strip all non-digit characters
        var digits = Regex.Replace(raw, @"\D", "");

        // 639XXXXXXXXX (12 digits) -> 09XXXXXXXXX
        if (digits.Length == 12 && digits.StartsWith("639"))
        {
            return "0" + digits.Substring(2);
        }

        // 9XXXXXXXXX (10 digits) -> 09XXXXXXXXX
        if (digits.Length == 10 && digits.StartsWith("9"))
        {
            return "0" + digits;
        }

        // 09XXXXXXXXX (11 digits)
        if (digits.Length == 11 && digits.StartsWith("09"))
        {
            return digits;
        }

        return null;
    }

    public async Task<bool> SendSmsAsync(
        string recipientPhoneNumber,
        string message,
        CancellationToken ct = default)
    {
        if (!_opts.Enabled)
        {
            _logger.LogDebug(
                "SMS sending is disabled (Sms:Enabled = false). Skipping SMS to {Phone}",
                MaskPhoneNumber(recipientPhoneNumber));
            return false;
        }

        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
        {
            _logger.LogWarning(
                "SMS is enabled but Semaphore API key is not configured. Skipping SMS to {Phone}.",
                MaskPhoneNumber(recipientPhoneNumber));
            return false;
        }

        var normalizedNumber = NormalizePhoneNumber(recipientPhoneNumber);
        if (string.IsNullOrWhiteSpace(normalizedNumber))
        {
            _logger.LogWarning(
                "Recipient phone number '{Phone}' is not a valid Philippine mobile number. Skipping SMS.",
                MaskPhoneNumber(recipientPhoneNumber));
            return false;
        }

        try
        {
            var formValues = new Dictionary<string, string>
            {
                ["apikey"]  = _opts.ApiKey,
                ["number"]  = normalizedNumber,
                ["message"] = message
            };

            if (!string.IsNullOrWhiteSpace(_opts.SenderName))
            {
                formValues["sendername"] = _opts.SenderName;
            }

            using var content = new FormUrlEncodedContent(formValues);
            var endpoint = !string.IsNullOrWhiteSpace(_opts.ApiUrl)
                ? _opts.ApiUrl
                : "https://api.semaphore.co/api/v4/messages";

            var response = await _http.PostAsync(endpoint, content, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "SMS successfully queued/sent to {Phone}",
                    MaskPhoneNumber(normalizedNumber));
                return true;
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Semaphore API returned status {StatusCode} for SMS to {Phone}. Response: {Response}",
                (int)response.StatusCode,
                MaskPhoneNumber(normalizedNumber),
                responseBody);

            return false;
        }
        catch (Exception ex)
        {
            // Never rethrow — SMS failures must never fail or block bookings/payments
            _logger.LogError(
                ex,
                "Failed to dispatch SMS to {Phone}",
                MaskPhoneNumber(recipientPhoneNumber));
            return false;
        }
    }

    private static string MaskPhoneNumber(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "[blank]";
        if (phone.Length <= 4) return "****";
        return string.Concat(phone.AsSpan(0, Math.Min(4, phone.Length)), "****", phone.AsSpan(Math.Max(0, phone.Length - 2)));
    }
}
