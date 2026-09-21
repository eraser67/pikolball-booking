using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

/// <summary>
/// Phase 25: composes and dispatches all booking and payment notification emails.
///
/// All sends are fire-and-forget (_= Task.Run) so SMTP latency never blocks
/// the HTTP response. IEmailService.SendAsync itself never throws.
///
/// Template design: inline CSS only (no external stylesheets) for maximum
/// email-client compatibility.
/// </summary>
public sealed class BookingEmailService
{
    private readonly IEmailService _email;
    private readonly ILogger<BookingEmailService> _logger;

    // Brand colors
    private const string Green      = "#16a34a";
    private const string GreenLight = "#dcfce7";
    private const string GrayText   = "#6b7280";
    private const string DarkText   = "#111827";
    private const string RedLight   = "#fee2e2";
    private const string Red        = "#dc2626";

    public BookingEmailService(IEmailService email, ILogger<BookingEmailService> logger)
    {
        _email  = email;
        _logger = logger;
    }

    // ─── Customer notifications ──────────────────────────────────────────────

    /// <summary>Booking received — sent immediately after booking is created.</summary>
    public void SendBookingReceivedAsync(Booking booking, Organization org)
        => Fire(_email.SendAsync(
            booking.CustomerEmail,
            booking.CustomerName,
            $"🏓 Booking Received — {booking.BookingReference}",
            BuildBookingReceivedHtml(booking, org)));

    /// <summary>Payment submitted — sent after customer submits GCash reference.</summary>
    public void SendPaymentSubmittedToCustomerAsync(Booking booking, Payment payment, Organization org)
        => Fire(_email.SendAsync(
            booking.CustomerEmail,
            booking.CustomerName,
            $"✅ Payment Received — {booking.BookingReference}",
            BuildPaymentSubmittedCustomerHtml(booking, payment, org)));

    /// <summary>Payment verified — sent after admin verifies the payment.</summary>
    public void SendPaymentVerifiedAsync(Booking booking, Organization org)
        => Fire(_email.SendAsync(
            booking.CustomerEmail,
            booking.CustomerName,
            $"🎉 Booking Confirmed — {booking.BookingReference}",
            BuildPaymentVerifiedHtml(booking, org)));

    /// <summary>Payment rejected — sent after admin rejects the payment.</summary>
    public void SendPaymentRejectedAsync(Booking booking, Payment payment, Organization org)
        => Fire(_email.SendAsync(
            booking.CustomerEmail,
            booking.CustomerName,
            $"⚠️ Payment Not Accepted — {booking.BookingReference}",
            BuildPaymentRejectedHtml(booking, payment, org)));

    /// <summary>Booking cancelled — sent after a booking is cancelled.</summary>
    public void SendBookingCancelledToCustomerAsync(Booking booking, Organization org)
        => Fire(_email.SendAsync(
            booking.CustomerEmail,
            booking.CustomerName,
            $"Booking Cancelled — {booking.BookingReference}",
            BuildBookingCancelledCustomerHtml(booking, org)));

    // ─── Organization notifications ──────────────────────────────────────────

    /// <summary>New booking alert — sent to org notification email after a booking is created.</summary>
    public void SendNewBookingToOrgAsync(Booking booking, Organization org)
    {
        if (string.IsNullOrWhiteSpace(org.NotificationEmail)) return;
        Fire(_email.SendAsync(
            org.NotificationEmail,
            org.Name,
            $"📋 New Booking — {booking.BookingReference}",
            BuildNewBookingOrgHtml(booking, org)));
    }

    /// <summary>Payment submitted alert — sent to org when customer submits proof.</summary>
    public void SendPaymentSubmittedToOrgAsync(Booking booking, Payment payment, Organization org)
    {
        if (string.IsNullOrWhiteSpace(org.NotificationEmail)) return;
        Fire(_email.SendAsync(
            org.NotificationEmail,
            org.Name,
            $"💳 Payment to Verify — {booking.BookingReference}",
            BuildPaymentSubmittedOrgHtml(booking, payment, org)));
    }

    /// <summary>Booking cancelled alert — sent to org when a booking is cancelled.</summary>
    public void SendBookingCancelledToOrgAsync(Booking booking, Organization org)
    {
        if (string.IsNullOrWhiteSpace(org.NotificationEmail)) return;
        Fire(_email.SendAsync(
            org.NotificationEmail,
            org.Name,
            $"❌ Booking Cancelled — {booking.BookingReference}",
            BuildBookingCancelledOrgHtml(booking, org)));
    }

    // ─── HTML templates ──────────────────────────────────────────────────────

    private static string BuildBookingReceivedHtml(Booking booking, Organization org)
    {
        var courtName  = booking.Court?.Name ?? "Court";
        var dateStr    = booking.BookingDate.ToString("MMMM d, yyyy");
        var timeStr    = FormatTimeRange(booking.StartTime, booking.EndTime);
        var priceStr   = booking.Price.ToString("C2");

        return Wrap(org.Name, $"""
            <h2 style="color:{Green};margin-bottom:8px;">Booking Received!</h2>
            <p style="color:{GrayText};">Hi {booking.CustomerName},</p>
            <p>Your court booking has been received. Please complete your payment to confirm your slot.</p>

            {DetailBox(new[] {
                ("Booking Reference", $"<strong>{booking.BookingReference}</strong>"),
                ("Court",             courtName),
                ("Date",              dateStr),
                ("Time",              timeStr),
                ("Amount Due",        $"<strong style='color:{Green};'>{priceStr}</strong>"),
            })}

            <p style="margin-top:20px;">To complete payment, visit your booking page and submit your GCash reference number.</p>
            <p style="color:{GrayText};font-size:13px;">If you have questions, please contact {org.Name}.</p>
        """);
    }

    private static string BuildPaymentSubmittedCustomerHtml(Booking booking, Payment payment, Organization org)
    {
        var submittedTime = payment.SubmittedAt.HasValue
            ? $"{AppClock.ToPhilippineTime(payment.SubmittedAt.Value):MMMM d, yyyy h:mm tt} (PST)"
            : $"{AppClock.NowLocal:MMMM d, yyyy h:mm tt} (PST)";

        return Wrap(org.Name, $"""
            <h2 style="color:{Green};margin-bottom:8px;">Payment Received!</h2>
            <p>Hi {booking.CustomerName},</p>
            <p>We've received your GCash payment submission for booking <strong>{booking.BookingReference}</strong>.</p>

            {DetailBox(new[] {
                ("Reference Number", payment.ReferenceNumber ?? "—"),
                ("Amount",           payment.Amount.ToString("C2")),
                ("Submitted At",     submittedTime),
                ("Status",           "Under review"),
            })}

            <p>Our team will verify your payment shortly. You'll receive another email once it's confirmed.</p>
            <p style="color:{GrayText};font-size:13px;">This usually takes a few minutes during business hours.</p>
        """);
    }

    private static string BuildPaymentVerifiedHtml(Booking booking, Organization org)
    {
        var courtName = booking.Court?.Name ?? "Court";
        var dateStr   = booking.BookingDate.ToString("MMMM d, yyyy");
        var timeStr   = FormatTimeRange(booking.StartTime, booking.EndTime);

        return Wrap(org.Name, $"""
            <h2 style="color:{Green};margin-bottom:8px;">🎉 Booking Confirmed!</h2>
            <p>Hi {booking.CustomerName},</p>
            <p>Great news! Your payment has been verified and your booking is now <strong>confirmed</strong>.</p>

            {DetailBox(new[] {
                ("Booking Reference", $"<strong>{booking.BookingReference}</strong>"),
                ("Court",             courtName),
                ("Date",              dateStr),
                ("Time",              timeStr),
                ("Status",            "<span style='color:#16a34a;font-weight:600;'>✅ Confirmed</span>"),
            })}

            <p style="margin-top:20px;">See you on the court! 🏓</p>
            <p style="color:{GrayText};font-size:13px;">Please arrive a few minutes before your scheduled time.</p>
        """);
    }

    private static string BuildPaymentRejectedHtml(Booking booking, Payment payment, Organization org)
    {
        var notes = string.IsNullOrWhiteSpace(payment.Notes)
            ? "No additional reason provided."
            : payment.Notes;

        return Wrap(org.Name, $"""
            <div style="background:{RedLight};border-left:4px solid {Red};padding:12px 16px;border-radius:6px;margin-bottom:20px;">
                <strong style="color:{Red};">Payment Not Accepted</strong>
            </div>
            <p>Hi {booking.CustomerName},</p>
            <p>Unfortunately, we were unable to verify your payment for booking <strong>{booking.BookingReference}</strong>.</p>

            {DetailBox(new[] {
                ("Reason", notes),
            })}

            <p style="margin-top:20px;">Please revisit your booking page and re-submit your payment with the correct GCash reference number and screenshot.</p>
            <p style="color:{GrayText};font-size:13px;">If you believe this is an error, please contact {org.Name} directly.</p>
        """);
    }

    private static string BuildBookingCancelledCustomerHtml(Booking booking, Organization org)
    {
        var dateStr = booking.BookingDate.ToString("MMMM d, yyyy");
        var timeStr = FormatTimeRange(booking.StartTime, booking.EndTime);

        return Wrap(org.Name, $"""
            <h2 style="margin-bottom:8px;">Booking Cancelled</h2>
            <p>Hi {booking.CustomerName},</p>
            <p>Your booking has been cancelled.</p>

            {DetailBox(new[] {
                ("Booking Reference", booking.BookingReference),
                ("Court",             booking.Court?.Name ?? "Court"),
                ("Date",              dateStr),
                ("Time",              timeStr),
            })}

            <p style="color:{GrayText};font-size:13px;">If you did not request this cancellation, please contact {org.Name}.</p>
        """);
    }

    private static string BuildNewBookingOrgHtml(Booking booking, Organization org)
    {
        var dateStr = booking.BookingDate.ToString("MMMM d, yyyy");
        var timeStr = FormatTimeRange(booking.StartTime, booking.EndTime);

        return Wrap(org.Name, $"""
            <h2 style="color:{Green};margin-bottom:8px;">New Booking Received</h2>
            <p>A new booking has been made on <strong>{org.Name}</strong>.</p>

            {DetailBox(new[] {
                ("Booking Reference", booking.BookingReference),
                ("Customer",          booking.CustomerName),
                ("Phone",             booking.CustomerPhone),
                ("Email",             booking.CustomerEmail),
                ("Court",             booking.Court?.Name ?? "Court"),
                ("Date",              dateStr),
                ("Time",              timeStr),
                ("Amount",            booking.Price.ToString("C2")),
            })}

            <p style="color:{GrayText};font-size:13px;">Log in to the admin panel to manage this booking.</p>
        """);
    }

    private static string BuildPaymentSubmittedOrgHtml(Booking booking, Payment payment, Organization org)
    {
        var submittedTime = payment.SubmittedAt.HasValue
            ? $"{AppClock.ToPhilippineTime(payment.SubmittedAt.Value):MMMM d, yyyy h:mm tt} (PST)"
            : $"{AppClock.NowLocal:MMMM d, yyyy h:mm tt} (PST)";

        return Wrap(org.Name, $"""
            <h2 style="color:{Green};margin-bottom:8px;">Payment Needs Verification</h2>
            <p>A customer has submitted a GCash payment for <strong>{org.Name}</strong>.</p>

            {DetailBox(new[] {
                ("Booking Reference", booking.BookingReference),
                ("Customer",          booking.CustomerName),
                ("GCash Reference",   payment.ReferenceNumber ?? "—"),
                ("Amount",            payment.Amount.ToString("C2")),
                ("Submitted At",      submittedTime),
            })}

            <p>Please log in to the admin panel to <strong>verify or reject</strong> this payment.</p>
        """);
    }

    private static string BuildBookingCancelledOrgHtml(Booking booking, Organization org)
    {
        var dateStr = booking.BookingDate.ToString("MMMM d, yyyy");
        var timeStr = FormatTimeRange(booking.StartTime, booking.EndTime);

        return Wrap(org.Name, $"""
            <h2 style="margin-bottom:8px;">Booking Cancelled</h2>
            <p>A booking has been cancelled on <strong>{org.Name}</strong>.</p>

            {DetailBox(new[] {
                ("Booking Reference", booking.BookingReference),
                ("Customer",          booking.CustomerName),
                ("Court",             booking.Court?.Name ?? "Court"),
                ("Date",              dateStr),
                ("Time",              timeStr),
            })}

            <p style="color:{GrayText};font-size:13px;">The time slot has been released and is now available for new bookings.</p>
        """);
    }

    // ─── Template helpers ─────────────────────────────────────────────────────

    private static string Wrap(string orgName, string content) => $"""
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
        <body style="margin:0;padding:0;background:#f9fafb;font-family:'Segoe UI',Arial,sans-serif;color:{DarkText};">
          <table width="100%" cellpadding="0" cellspacing="0" style="background:#f9fafb;padding:32px 16px;">
            <tr><td align="center">
              <table width="100%" style="max-width:560px;background:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 1px 6px rgba(0,0,0,.08);">

                <!-- Header -->
                <tr><td style="background:{Green};padding:24px 32px;">
                  <h1 style="margin:0;color:#ffffff;font-size:22px;font-weight:700;">{orgName}</h1>
                </td></tr>

                <!-- Body -->
                <tr><td style="padding:28px 32px;line-height:1.65;font-size:15px;">
                  {content}
                </td></tr>

                <!-- Footer -->
                <tr><td style="background:#f3f4f6;padding:16px 32px;font-size:12px;color:{GrayText};text-align:center;">
                  This email was sent by {orgName} via Pikolball Booking. Please do not reply to this email.
                </td></tr>

              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;

    private static string DetailBox(IEnumerable<(string Label, string Value)> rows)
    {
        var rowsHtml = string.Join("", rows.Select(r => $"""
            <tr>
              <td style="padding:6px 0;color:{GrayText};font-size:13px;white-space:nowrap;width:150px;">{r.Label}</td>
              <td style="padding:6px 0;font-weight:500;">{r.Value}</td>
            </tr>
            """));

        return $"""
            <table style="width:100%;border-collapse:collapse;background:{GreenLight};border-radius:8px;padding:16px;margin:16px 0;" cellpadding="0" cellspacing="0">
              <tr><td style="padding:16px;">
                <table style="width:100%;border-collapse:collapse;">
                  {rowsHtml}
                </table>
              </td></tr>
            </table>
            """;
    }

    private static string FormatTimeRange(TimeSpan start, TimeSpan end)
    {
        static string Fmt(TimeSpan t)
        {
            var dt = DateTime.Today.Add(t == TimeSpan.Zero ? TimeSpan.FromHours(24) : t);
            return dt.ToString("h:mm tt");
        }
        return $"{Fmt(start)} – {Fmt(end)}";
    }

    private static void Fire(Task task)
        => _ = task.ContinueWith(
            t => { /* already logged inside IEmailService.SendAsync */ },
            TaskContinuationOptions.OnlyOnFaulted);
}
