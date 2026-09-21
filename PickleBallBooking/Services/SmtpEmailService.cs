using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace PickleBallBooking.Services;

/// <summary>
/// Phase 25: MailKit SMTP implementation of IEmailService.
///
/// SECURITY: Credentials are read from IOptions (User Secrets / env vars).
/// They are NEVER logged, exposed in errors, or sent to the client.
///
/// RELIABILITY: All exceptions are caught and logged. This service never throws,
/// making it safe to call fire-and-forget from BookingService/PaymentService
/// without delaying the booking or payment response.
/// </summary>
public sealed class SmtpEmailService : IEmailService
{
    private readonly EmailOptions _opts;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IOptions<EmailOptions> opts, ILogger<SmtpEmailService> logger)
    {
        _opts   = opts.Value;
        _logger = logger;
    }

    public async Task SendAsync(
        string toAddress,
        string toName,
        string subject,
        string htmlBody,
        CancellationToken ct = default)
    {
        if (!_opts.Enabled)
        {
            _logger.LogDebug("Email sending is disabled (Email:Enabled = false). Skipping email to {To} — {Subject}", toAddress, subject);
            return;
        }

        if (string.IsNullOrWhiteSpace(_opts.FromAddress) ||
            string.IsNullOrWhiteSpace(_opts.Username) ||
            string.IsNullOrWhiteSpace(_opts.Password))
        {
            _logger.LogWarning("Email is enabled but SMTP credentials are not fully configured. Skipping email to {To}.", toAddress);
            return;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_opts.FromName, _opts.FromAddress));
            message.To.Add(new MailboxAddress(toName, toAddress));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(_opts.SmtpHost, _opts.SmtpPort, SecureSocketOptions.StartTls, ct);
            await client.AuthenticateAsync(_opts.Username, _opts.Password, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(quit: true, ct);

            _logger.LogInformation("Email sent to {To} — {Subject}", toAddress, subject);
        }
        catch (Exception ex)
        {
            // Never rethrow — a broken SMTP server must not fail the booking/payment.
            _logger.LogError(ex, "Failed to send email to {To} — {Subject}", toAddress, subject);
        }
    }
}
