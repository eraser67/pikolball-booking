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
    /// Alert staff when the AI agent could not auto-verify a payment and manual review is needed.
    /// </summary>
    public void SendAiManualReviewAlertAsync(Booking booking, Payment payment, Organization org, string aiReason)
    {
        var chatId = ResolveChatId(org);
        if (string.IsNullOrWhiteSpace(chatId)) return;

        var message = BuildAiManualReviewAlertMessage(booking, payment, org, aiReason);
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

    // ─── Phase 36: Activity alerts ────────────────────────────────────────────

    /// <summary>
    /// Alert staff when a player registers for a community activity.
    /// </summary>
    public void SendActivityNewRegistrationAlertAsync(Activity activity, Organization org, string playerName)
    {
        var chatId = ResolveChatId(org);
        if (string.IsNullOrWhiteSpace(chatId)) return;

        var orgName     = EscapeMarkdown(org.Name);
        var actName     = EscapeMarkdown(activity.Name);
        var playerEsc   = EscapeMarkdown(playerName);

        var message = $"""
            📋 *New Activity Registration*
            🏢 *Club*: {orgName}
            🏓 *Activity*: {actName}
            📅 *Date*: {activity.Date:MMMM d, yyyy}
            👤 *Player*: {playerEsc}
            """;

        Fire(_telegram.SendMessageAsync(chatId, message));
    }

    /// <summary>
    /// Alert staff when a player cancels their RSVP for a community activity.
    /// </summary>
    public void SendActivityRsvpCancelledAlertAsync(
        Activity activity,
        Organization org,
        string playerName,
        string? promotedPlayerName = null)
    {
        var chatId = ResolveChatId(org);
        if (string.IsNullOrWhiteSpace(chatId)) return;

        var orgName     = EscapeMarkdown(org.Name);
        var actName     = EscapeMarkdown(activity.Name);
        var playerEsc   = EscapeMarkdown(playerName);

        var promotionLine = !string.IsNullOrWhiteSpace(promotedPlayerName)
            ? $"\n⚡ *Waitlist Auto-Promoted*: {EscapeMarkdown(promotedPlayerName)}"
            : string.Empty;

        var message = $"""
            ⚠️ *Activity RSVP Cancelled*
            🏢 *Club*: {orgName}
            🏓 *Activity*: {actName}
            📅 *Date*: {activity.Date:MMMM d, yyyy}
            👤 *Player*: {playerEsc}{promotionLine}
            """;

        Fire(_telegram.SendMessageAsync(chatId, message));
    }

    /// <summary>
    /// Alert staff (and optionally log) when an activity is cancelled — sent once per cancellation event.
    /// </summary>
    public void SendActivityCancelledAlertAsync(Activity activity, Organization org)
    {
        var chatId = ResolveChatId(org);
        if (string.IsNullOrWhiteSpace(chatId)) return;

        var orgName  = EscapeMarkdown(org.Name);
        var actName  = EscapeMarkdown(activity.Name);

        var message = $"""
            ❌ *Activity Cancelled*
            🏢 *Club*: {orgName}
            🏓 *Activity*: {actName}
            📅 *Date*: {activity.Date:MMMM d, yyyy}
            ⚡ All registered players have been notified by email.
            """;

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

    public static string BuildAiManualReviewAlertMessage(Booking booking, Payment payment, Organization org, string aiReason)
    {
        var orgName  = EscapeMarkdown(org.Name);
        var custName = EscapeMarkdown(booking.CustomerName);
        var refNum   = EscapeMarkdown(payment.ReferenceNumber);
        var amount   = payment.Amount.ToString("C2");
        var reason   = EscapeMarkdown(aiReason[..Math.Min(aiReason.Length, 200)]);

        return $"""
            🤖 *AI Could Not Auto\\-Verify Payment*
            🏢 *Club*: {orgName}
            📋 *Booking*: {EscapeMarkdown(booking.BookingReference)}
            👤 *Customer*: {custName}
            🧾 *Ref\\#*: `{refNum}`
            💰 *Amount*: {EscapeMarkdown(amount)}
            ⚠️ *AI Reason*: {reason}
            ⚡ *Action Required*: Please review the payment proof and verify or reject manually.
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
