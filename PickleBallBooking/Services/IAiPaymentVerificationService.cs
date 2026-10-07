using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

// ─────────────────────────────────────────────────────────────────────────────
// Result types
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Outcome of the AI payment verification cascade.
/// Decision is either AutoVerify or ManualReview — the AI never auto-rejects.
/// </summary>
public sealed record AiVerificationResult(
    /// <summary>Whether to auto-verify the payment or queue it for manual admin review.</summary>
    AiVerificationDecision Decision,

    /// <summary>Confidence score from 0.0 (no confidence) to 1.0 (fully confident).</summary>
    double Confidence,

    /// <summary>Human-readable explanation of the decision (stored as payment Notes).</summary>
    string Reason,

    /// <summary>Which provider produced this result: "GeminiFlash", "GeminiPro", "Deepseek", or "RulesOnly".</summary>
    string ProviderUsed,

    /// <summary>True when a proof image was analyzed visually (as opposed to text/rules only).</summary>
    bool VisionUsed);

/// <summary>
/// What the AI agent should do with the payment.
/// The AI never auto-rejects — uncertain cases become ManualReview.
/// </summary>
public enum AiVerificationDecision
{
    /// <summary>Confidence ≥ threshold → call PaymentService.VerifyAsync() automatically.</summary>
    AutoVerify,

    /// <summary>Confidence below threshold → notify admin and leave in manual queue.</summary>
    ManualReview
}

// ─────────────────────────────────────────────────────────────────────────────
// Interface
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Evaluates a submitted payment using a multi-provider AI cascade
/// (Gemini Flash → Gemini Pro → Deepseek → rule-based fallback).
///
/// IMPORTANT — this service NEVER auto-rejects:
///   - AutoVerify  → PaymentService.VerifyAsync() is called automatically.
///   - ManualReview → admin is notified via email + Telegram; payment stays in queue.
/// </summary>
public interface IAiPaymentVerificationService
{
    /// <summary>
    /// Evaluates the submitted payment. Runs rule-based checks first, then
    /// optionally calls AI vision if a proof image URL is provided.
    /// </summary>
    /// <param name="payment">The payment record (must be in Submitted status).</param>
    /// <param name="booking">The associated booking (loaded with Court).</param>
    /// <param name="proofImageSignedUrl">
    /// Time-limited signed URL for the proof screenshot. Pass null when no image was uploaded.
    /// </param>
    Task<AiVerificationResult> EvaluateAsync(
        Payment payment,
        Booking booking,
        string? proofImageSignedUrl,
        string? expectedRecipientName = null,
        string? expectedRecipientNumber = null,
        CancellationToken ct = default);
}
