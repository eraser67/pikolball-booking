namespace PickleBallBooking.Services;

/// <summary>
/// Configuration for the AI payment verification cascade.
/// Bind from the "AiVerification" section in appsettings.json / User Secrets.
/// Providers are tried in order; the first healthy response wins.
/// </summary>
public class AiVerificationOptions
{
    public const string SectionName = "AiVerification";

    /// <summary>
    /// Ordered list of AI providers. First provider with a non-empty ApiKey is tried first.
    /// If it fails (rate limit, error, timeout), the next is tried automatically.
    /// </summary>
    public List<AiProviderConfig> Providers { get; set; } = [];

    /// <summary>
    /// Minimum confidence score (0.0–1.0) to auto-verify a payment.
    /// Results below this threshold fall back to manual admin review.
    /// </summary>
    public double ConfidenceThreshold { get; set; } = 0.85;

    /// <summary>Per-provider HTTP timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 20;
}

/// <summary>Configuration for a single AI provider in the cascade.</summary>
public class AiProviderConfig
{
    /// <summary>The provider type — determines which API endpoint and format to use.</summary>
    public AiProviderType Type { get; set; }

    /// <summary>
    /// API key for this provider. Leave empty to skip this provider.
    /// Set via User Secrets or environment variables — never commit to source control.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Optional model override. If null, the service uses the default for each provider type.
    /// Gemini: "gemini-2.0-flash" / "gemini-2.5-pro"
    /// Deepseek: "deepseek-chat"
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Whether to send the payment proof image to this provider for visual analysis.
    /// Deepseek text models do not support vision — set to false for those.
    /// </summary>
    public bool EnableVision { get; set; } = true;
}

/// <summary>Supported AI provider types for the payment verification cascade.</summary>
public enum AiProviderType
{
    /// <summary>Google Gemini Flash — fast, free tier, supports vision.</summary>
    GeminiFlash,

    /// <summary>Google Gemini Pro — higher accuracy, Google AI Pro plan, supports vision.</summary>
    GeminiPro,

    /// <summary>Deepseek Chat — text-only fallback (no vision support).</summary>
    Deepseek,

    /// <summary>OpenRouter — supports free and multimodal vision models.</summary>
    OpenRouter
}
