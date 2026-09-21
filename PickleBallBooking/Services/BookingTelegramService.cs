using Microsoft.Extensions.Options;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

/// <summary>
/// Composes and dispatches real-time Telegram alerts to organization admins and court staff.
///
/// All sends are fire-and-forget so external Telegram API latency never blocks
/// the HTTP response. ITelegramService.SendMessageAsync itself never throws.
/// </summary>
public sealed class BookingTelegramService
{
    private readonly ITelegramService _telegram;
    private readonly TelegramOptions _opts;
    private readonly ILogger<BookingTelegramService> _logger;

    public BookingTelegramService(
        ITelegramService telegram,
        IOptions<TelegramOptions> opts,
        ILogger<BookingTelegramService> logger)
    {
        _telegram = telegram;
        _opts     = opts.Value;
        _logger   = logger;
    }

    /// <summary>
    /// Alert staff when a new booking is created.
    /// </summary>
    public void SendNewBookingAlertAsync(Booking booking, Organization org)
    {
        var chatId = ResolveChatId(org);
        if (string.IsNullOrWhiteSpace(chatId)) return;

        var message = BuildNewBookingAlertMessage(booking, org);
        Fire(_telegram.SendMessageAsync(chatId, message));
    }

    /// <summary>
    /// Alert staff when a player submits GCash payment proof.
    /// </summary>
    public void SendPaymentSubmittedAlertAsync(Booking booking, Payment payment, Organization org)
    {
        var chatId = ResolveChatId(org);
        if (string.IsNullOrWhiteSpace(chatId)) return;

        var message = BuildPaymentSubmittedAlertMessage(booking, payment, org);
        Fire(_telegram.SendMessageAsync(chatId, message));
    }

    /// <summary>
    /// Alert staff when a booking is cancelled.
    /// </summary>
    public void SendBookingCancelledAlertAsync(Booking booking, Organization org)
    {
        var chatId = ResolveChatId(org);
        if (string.IsNullOrWhiteSpace(chatId)) return;

        var message = BuildBookingCancelledAlertMessage(booking, org);
        Fire(_telegram.SendMessageAsync(chatId, message));
    }

    private string? ResolveChatId(Organization org)
        => !string.IsNullOrWhiteSpace(org.TelegramChatId)
            ? org.TelegramChatId
            : _opts.DefaultChatId;

    // ─── Alert Message Builders ──────────────────────────────────────────────

    public static string BuildNewBookingAlertMessage(Booking booking, Organization org)
    {
        var courtName = EscapeMarkdown(booking.Court?.Name ?? "Court");
        var orgName   = EscapeMarkdown(org.Name);
        var custName  = EscapeMarkdown(booking.CustomerName);
        var custPhone = EscapeMarkdown(string.IsNullOrWhiteSpace(booking.CustomerPhone) ? "N/A" : booking.CustomerPhone);
        var timeStr   = EscapeMarkdown(FormatTimeRange(booking.StartTime, booking.EndTime));
        var priceStr  = booking.Price.ToString("C2");

        return $"""
            📋 *New Booking Received* — {EscapeMarkdown(booking.BookingReference)}
            🏢 *Club*: {orgName}
            👤 *Customer*: {custName}
            📱 *Phone*: {custPhone}
            🏓 *Court*: {courtName}
            📅 *Date*: {booking.BookingDate:MMMM d, yyyy}
            ⏰ *Time*: {timeStr}
            💰 *Total*: {EscapeMarkdown(priceStr)}
            """;
    }

    public static string BuildPaymentSubmittedAlertMessage(Booking booking, Payment payment, Organization org)
    {
        var orgName  = EscapeMarkdown(org.Name);
        var custName = EscapeMarkdown(booking.CustomerName);
        var refNum   = EscapeMarkdown(payment.ReferenceNumber);
        var amount   = payment.Amount.ToString("C2");

        return $"""
            💳 *Payment Proof Submitted* — {EscapeMarkdown(booking.BookingReference)}
            🏢 *Club*: {orgName}
            👤 *Customer*: {custName}
            🧾 *GCash Ref*: `{refNum}`
            💰 *Amount*: {EscapeMarkdown(amount)}
            ⚡ *Action*: Please review and verify payment in Admin Dashboard.
            """;
    }

    public static string BuildBookingCancelledAlertMessage(Booking booking, Organization org)
    {
        var courtName = EscapeMarkdown(booking.Court?.Name ?? "Court");
        var orgName   = EscapeMarkdown(org.Name);
        var custName  = EscapeMarkdown(booking.CustomerName);

        return $"""
            ❌ *Booking Cancelled* — {EscapeMarkdown(booking.BookingReference)}
            🏢 *Club*: {orgName}
            👤 *Customer*: {custName}
            🏓 *Court*: {courtName}
            📅 *Date*: {booking.BookingDate:MMMM d, yyyy}
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

    private static string EscapeMarkdown(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        // Escape characters with special meaning in Telegram legacy Markdown: _ * [ `
        return input
            .Replace("_", @"\_")
            .Replace("*", @"\*")
            .Replace("[", @"\[")
            .Replace("`", @"\`");
    }

    private static void Fire(Task task)
        => _ = task.ContinueWith(
            t => { /* already logged inside ITelegramService.SendMessageAsync */ },
            TaskContinuationOptions.OnlyOnFaulted);
}
