using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

/// <summary>
/// Composes and dispatches booking and payment SMS notifications to customers.
///
/// All sends are fire-and-forget so SMS gateway latency never blocks
/// the HTTP response. ISmsService.SendSmsAsync itself never throws.
/// </summary>
public sealed class BookingSmsService
{
    private readonly ISmsService _sms;
    private readonly ILogger<BookingSmsService> _logger;

    public BookingSmsService(ISmsService sms, ILogger<BookingSmsService> logger)
    {
        _sms    = sms;
        _logger = logger;
    }

    /// <summary>Booking received — sent immediately after a customer books a slot.</summary>
    public void SendBookingReceivedAsync(Booking booking, Organization org)
    {
        if (string.IsNullOrWhiteSpace(booking.CustomerPhone)) return;
        var msg = BuildBookingReceivedMessage(booking, org);
        Fire(_sms.SendSmsAsync(booking.CustomerPhone, msg));
    }

    /// <summary>Payment verified — sent after admin verifies payment.</summary>
    public void SendPaymentVerifiedAsync(Booking booking, Organization org)
    {
        if (string.IsNullOrWhiteSpace(booking.CustomerPhone)) return;
        var msg = BuildPaymentVerifiedMessage(booking, org);
        Fire(_sms.SendSmsAsync(booking.CustomerPhone, msg));
    }

    /// <summary>Payment rejected — sent after admin rejects payment proof.</summary>
    public void SendPaymentRejectedAsync(Booking booking, Payment payment, Organization org)
    {
        if (string.IsNullOrWhiteSpace(booking.CustomerPhone)) return;
        var msg = BuildPaymentRejectedMessage(booking, org);
        Fire(_sms.SendSmsAsync(booking.CustomerPhone, msg));
    }

    /// <summary>Booking cancelled — sent after a booking is cancelled.</summary>
    public void SendBookingCancelledToCustomerAsync(Booking booking, Organization org)
    {
        if (string.IsNullOrWhiteSpace(booking.CustomerPhone)) return;
        var msg = BuildBookingCancelledMessage(booking, org);
        Fire(_sms.SendSmsAsync(booking.CustomerPhone, msg));
    }

    // ─── SMS Message Builders ────────────────────────────────────────────────

    public static string BuildBookingReceivedMessage(Booking booking, Organization org)
    {
        var courtName = booking.Court?.Name ?? "Court";
        var timeStr   = FormatTimeRange(booking.StartTime, booking.EndTime);
        return $"Hi {booking.CustomerName}, your booking {booking.BookingReference} at {org.Name} ({courtName}, {booking.BookingDate:MMM d}, {timeStr}) is received! Please submit payment to confirm.";
    }

    public static string BuildPaymentVerifiedMessage(Booking booking, Organization org)
    {
        var courtName = booking.Court?.Name ?? "Court";
        var timeStr   = FormatTimeRange(booking.StartTime, booking.EndTime);
        return $"Hi {booking.CustomerName}, booking {booking.BookingReference} at {org.Name} ({courtName}, {booking.BookingDate:MMM d}, {timeStr}) is CONFIRMED! See you on court.";
    }

    public static string BuildPaymentRejectedMessage(Booking booking, Organization org)
    {
        return $"Hi {booking.CustomerName}, payment for {booking.BookingReference} at {org.Name} was not accepted. Please check details or resubmit payment proof.";
    }

    public static string BuildBookingCancelledMessage(Booking booking, Organization org)
    {
        return $"Hi {booking.CustomerName}, your booking {booking.BookingReference} at {org.Name} for {booking.BookingDate:MMM d} has been cancelled.";
    }

    private static string FormatTimeRange(TimeSpan start, TimeSpan end)
    {
        static string Fmt(TimeSpan t)
        {
            var dt = DateTime.Today.Add(t == TimeSpan.Zero ? TimeSpan.FromHours(24) : t);
            return dt.ToString("h:mm tt");
        }
        return $"{Fmt(start)} - {Fmt(end)}";
    }

    private static void Fire(Task task)
        => _ = task.ContinueWith(
            t => { /* already logged inside ISmsService.SendSmsAsync */ },
            TaskContinuationOptions.OnlyOnFaulted);
}
