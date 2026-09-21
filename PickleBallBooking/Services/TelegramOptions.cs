namespace PickleBallBooking.Services;

/// <summary>
/// Telegram Bot configuration, bound from the "Telegram" appsettings section.
/// Never commit bot tokens to source control — configure via User Secrets or environment variables (Telegram__BotToken).
/// </summary>
public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    /// <summary>
    /// Telegram Bot token obtained from @BotFather (e.g. "123456789:ABCdefGhIJKlmNoPQRsTUVwxyZ").
    /// </summary>
    public string BotToken { get; init; } = string.Empty;

    /// <summary>
    /// Optional default/fallback Chat ID or Group ID (e.g. "-1001234567890" or "123456789").
    /// Used if an organization does not specify its own TelegramChatId in OrgSettings.
    /// </summary>
    public string? DefaultChatId { get; init; }

    /// <summary>
    /// Set to true to enable Telegram staff notifications. Defaults to false.
    /// </summary>
    public bool Enabled { get; init; }
}
