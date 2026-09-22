using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace PickleBallBooking.Services;

/// <summary>
/// IEmailService implementation that sends transactional emails via the Resend REST API.
/// https://resend.com/docs/api-reference/emails/send-email
///
/// SECURITY: The API key is read from IOptions (User Secrets / env vars).
///           It is NEVER logged, exposed in errors, or sent to the client.
///
/// RELIABILITY: All exceptions are caught and logged. This service never throws,
///              making it safe to call fire-and-forget from BookingEmailService
///              without delaying the booking or payment HTTP response.
/// </summary>
public sealed class ResendEmailService : IEmailService
{
    private const string ResendEndpoint = "https://api.resend.com/emails";

    private readonly HttpClient _http;
    private readonly EmailOptions _opts;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(
        HttpClient http,
        IOptions<EmailOptions> opts,
        ILogger<ResendEmailService> logger)
    {
        _http   = http;
        _opts   = opts.Value;
        _logger = logger;
    }

    public async Task SendAsync(
        string toAddress,
        string toName,
        string subject,
        string htmlBody,
        string? fromAddress = null,
        string? fromName = null,
        string? replyTo = null,
        CancellationToken ct = default)
    {
        if (!_opts.Enabled)
        {
            _logger.LogDebug(
                "Email sending is disabled (Email:Enabled = false). Skipping email to {To} — {Subject}",
                toAddress, subject);
            return;
        }

        var effectiveFromAddress = !string.IsNullOrWhiteSpace(fromAddress) ? fromAddress : _opts.FromAddress;
        var effectiveFromName = !string.IsNullOrWhiteSpace(fromName) ? fromName : _opts.FromName;

        if (string.IsNullOrWhiteSpace(_opts.ApiKey) || string.IsNullOrWhiteSpace(effectiveFromAddress))
        {
            _logger.LogWarning(
                "Email is enabled but Resend credentials are not fully configured. Skipping email to {To}.",
                toAddress);
            return;
        }

        try
        {
            object payload;
            if (!string.IsNullOrWhiteSpace(replyTo))
            {
                payload = new
                {
                    from     = $"{effectiveFromName} <{effectiveFromAddress}>",
                    to       = new[] { $"{toName} <{toAddress}>" },
                    subject,
                    html     = htmlBody,
                    reply_to = replyTo
                };
            }
            else
            {
                payload = new
                {
                    from = $"{effectiveFromName} <{effectiveFromAddress}>",
                    to   = new[] { $"{toName} <{toAddress}>" },
                    subject,
                    html = htmlBody
                };
            }

            var json    = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, ResendEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiKey);
            request.Content = content;

            var response = await _http.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Email sent from {From} to {To} — {Subject}", $"{effectiveFromName} <{effectiveFromAddress}>", toAddress, subject);
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError(
                    "Resend API returned {StatusCode} for email to {To} — {Subject}. Body: {Body}",
                    (int)response.StatusCode, toAddress, subject, body);
            }
        }
        catch (Exception ex)
        {
            // Never rethrow — a broken email service must not fail the booking/payment.
            _logger.LogError(ex, "Failed to send email to {To} — {Subject}", toAddress, subject);
        }
    }
}
