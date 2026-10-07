using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

/// <summary>
/// Multi-provider AI cascade for payment verification.
///
/// Cascade order (first provider with a non-empty ApiKey wins):
///   1. Gemini Flash  (google — fast, free tier, vision)
///   2. Gemini Pro    (google — higher accuracy, Google AI Pro, vision)
///   3. Deepseek      (text-only fallback — no vision)
///   4. Rule-based    (always available — zero API cost)
///
/// The AI NEVER auto-rejects. Below-threshold confidence → ManualReview
/// so the admin always has the final say.
/// </summary>
public sealed class AiPaymentVerificationService : IAiPaymentVerificationService
{
    private readonly AiVerificationOptions _options;
    private readonly HttpClient             _http;
    private readonly ILogger<AiPaymentVerificationService> _logger;

    // Gemini REST base — use v1beta for modern Gemini models (gemini-3.8-flash, etc.)
    private const string GeminiBaseUrl  = "https://generativelanguage.googleapis.com/v1beta/models/";
    // Deepseek REST base
    private const string DeepseekApiUrl = "https://api.deepseek.com/chat/completions";
    // OpenRouter REST base
    private const string OpenRouterApiUrl = "https://openrouter.ai/api/v1/chat/completions";

    // Default model names (gemini-2.0 is deprecated/discontinued; gemini-3.8-flash is current)
    private const string DefaultGeminiFlashModel = "gemini-3.8-flash";
    private const string DefaultGeminiProModel   = "gemini-3.1-pro-preview";
    private const string DefaultDeepseekModel    = "deepseek-chat";
    private const string DefaultOpenRouterModel  = "dots-studio/dots-3-note-preview:free";

    public AiPaymentVerificationService(
        HttpClient http,
        IOptions<AiVerificationOptions> options,
        ILogger<AiPaymentVerificationService> logger)
    {
        _http    = http;
        _options = options.Value;
        _logger  = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public entry point
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<AiVerificationResult> EvaluateAsync(
        Payment payment,
        Booking booking,
        string? proofImageSignedUrl,
        string? expectedRecipientName = null,
        string? expectedRecipientNumber = null,
        CancellationToken ct = default)
    {
        // Phase 1: Rule-based checks — always run, zero cost
        var ruleResult = RunRuleChecks(payment, booking);

        // Hard rule failure (e.g. empty reference, invalid format, zero amount) → skip AI entirely
        if (ruleResult.Confidence == 0.0)
        {
            _logger.LogWarning(
                "Payment {Id} failed rule checks before AI: {Reason}",
                payment.Id, ruleResult.Reason);
            return ruleResult;
        }

        // Start bestResult with ruleResult (which has confidence 0.50, ManualReview).
        // CRITICAL: Rule checks alone NEVER auto-verify! AutoVerify requires visual confirmation of the receipt.
        var bestResult = ruleResult;

        var activeProviders = _options.Providers
            .Where(p => !string.IsNullOrWhiteSpace(p.ApiKey))
            .ToList();

        if (activeProviders.Count == 0)
        {
            _logger.LogWarning(
                "No AI providers configured. Payment {Id} queued for manual admin review.",
                payment.Id);
            return bestResult;
        }

        foreach (var provider in activeProviders)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            try
            {
                var result = provider.Type switch
                {
                    AiProviderType.GeminiFlash or AiProviderType.GeminiPro
                        => await CallGeminiAsync(provider, payment, booking, proofImageSignedUrl, expectedRecipientName, expectedRecipientNumber, cts.Token),

                    AiProviderType.Deepseek
                        => await CallDeepseekAsync(provider, payment, booking, proofImageSignedUrl is not null, expectedRecipientName, expectedRecipientNumber, cts.Token),

                    AiProviderType.OpenRouter
                        => await CallOpenRouterAsync(provider, payment, booking, proofImageSignedUrl, expectedRecipientName, expectedRecipientNumber, cts.Token),

                    _ => throw new NotSupportedException($"Unknown provider type: {provider.Type}")
                };

                _logger.LogInformation(
                    "Payment {Id}: {Provider} → {Decision} (confidence={Confidence:P0}) | {Reason}",
                    payment.Id, result.ProviderUsed, result.Decision, result.Confidence, result.Reason);

                // If AI detected a discrepancy / failure (wrong amount, wrong recipient, fake receipt),
                // AI's low confidence ManualReview immediately overrides the default rule result and halts!
                if (result.Decision == AiVerificationDecision.ManualReview && result.Confidence <= 0.20)
                {
                    bestResult = result;
                    _logger.LogWarning(
                        "Payment {Id}: AI detected discrepancy or mismatch ({Reason}). Halting cascade for manual review.",
                        payment.Id, result.Reason);
                    break;
                }

                // If AI verified with higher confidence:
                if (result.Confidence > bestResult.Confidence)
                {
                    bestResult = result;
                }

                // If we achieved high-confidence AutoVerify with vision:
                if (bestResult.Confidence >= _options.ConfidenceThreshold &&
                    bestResult.Decision == AiVerificationDecision.AutoVerify)
                {
                    _logger.LogInformation(
                        "Payment {Id}: confidence threshold met ({Confidence:P0}). Stopping cascade.",
                        payment.Id, bestResult.Confidence);
                    break;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Payment {Id}: provider {Provider} timed out after {Sec}s. Trying next.",
                    payment.Id, provider.Type, _options.TimeoutSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Payment {Id}: provider {Provider} threw an error. Trying next.",
                    payment.Id, provider.Type);
            }
        }

        _logger.LogInformation(
            "Payment {Id}: cascade complete. Best result = {Decision} ({Confidence:P0}) from {Provider}.",
            payment.Id, bestResult.Decision, bestResult.Confidence, bestResult.ProviderUsed);

        return bestResult;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Rule-based checks (zero API cost, always runs first)
    // ─────────────────────────────────────────────────────────────────────────

    private static AiVerificationResult RunRuleChecks(Payment payment, Booking booking)
    {
        // Rule 1: Reference number must be present
        var refNum = (payment.ReferenceNumber ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(refNum))
            return ManualReview(0.0, "No reference number submitted.", "RulesOnly");

        // Rule 2: Reference must look like a payment reference
        // GCash = 13 digits | Maya/PayMaya = varies | Bank transfer = alphanumeric 6–30 chars
        if (!Regex.IsMatch(refNum, @"^\d{10,20}$|^[A-Z0-9a-z\-_]{6,30}$"))
            return ManualReview(0.0,
                $"Reference number format does not match any known payment provider pattern: '{refNum}'.",
                "RulesOnly");

        // Rule 3: Amount must be positive and match booking price
        if (payment.Amount <= 0)
            return ManualReview(0.0, "Payment amount is zero or negative.", "RulesOnly");

        if (payment.Amount != booking.Price)
            return ManualReview(0.0,
                $"Payment amount ₱{payment.Amount:F2} does not match booking price ₱{booking.Price:F2}.",
                "RulesOnly");

        // Rule 4: Submission timestamp sanity check
        if (payment.SubmittedAt.HasValue &&
            payment.SubmittedAt.Value > DateTime.UtcNow.AddMinutes(5))
            return ManualReview(0.0, "Submission timestamp is in the future.", "RulesOnly");

        // All format rules passed — moderate confidence; requires AI vision verification of receipt.
        // NOTE: Rules alone NEVER auto-verify! AutoVerify requires visual confirmation of the receipt.
        return ManualReview(
            0.50,
            "Reference number format is valid and booking fee matches. Requires AI vision receipt verification.",
            "RulesOnly");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gemini (Flash or Pro) — REST API, supports vision
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<AiVerificationResult> CallGeminiAsync(
        AiProviderConfig provider,
        Payment payment,
        Booking booking,
        string? proofImageSignedUrl,
        string? expectedRecipientName,
        string? expectedRecipientNumber,
        CancellationToken ct)
    {
        var model = provider.Model ?? (provider.Type == AiProviderType.GeminiPro
            ? DefaultGeminiProModel
            : DefaultGeminiFlashModel);

        var providerName = provider.Type.ToString();
        var useVision    = provider.EnableVision && !string.IsNullOrEmpty(proofImageSignedUrl);

        // Build the parts array for Gemini content
        var parts = new List<object>
        {
            new { text = BuildVerificationPrompt(payment, booking, expectedRecipientName, expectedRecipientNumber, useVision, proofImageSignedUrl is not null) }
        };

        // Add image part if we have a proof URL and vision is enabled
        if (useVision && proofImageSignedUrl is not null)
        {
            // Download the image bytes so we can send as inline data
            // (Gemini accepts inline base64 OR a file URI — we use inline for simplicity)
            try
            {
                var imageBytes = await _http.GetByteArrayAsync(proofImageSignedUrl, ct);
                var base64     = Convert.ToBase64String(imageBytes);
                // Try to detect MIME type from common extensions
                var mimeType   = proofImageSignedUrl.Contains(".png") ? "image/png" : "image/jpeg";

                parts.Add(new
                {
                    inline_data = new { mime_type = mimeType, data = base64 }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not download proof image for Gemini vision. Proceeding with text-only prompt.");
                useVision = false;
            }
        }

        var requestBody = new
        {
            contents = new[]
            {
                new { role = "user", parts }
            },
            generationConfig = new
            {
                temperature      = 0.1,    // low temperature = deterministic, factual
                maxOutputTokens  = 512,
                responseMimeType = "application/json"
            }
        };

        var url  = $"{GeminiBaseUrl}{model}:generateContent?key={provider.ApiKey}";
        var json = JsonSerializer.Serialize(requestBody);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        // For AQ. format keys, try all three authentication methods Google supports:
        // 1. ?key= query param (already in URL above)
        // 2. x-goog-api-key header (recommended by Google for newer key formats)
        // 3. Authorization: Bearer (OAuth2-style fallback)
        if (provider.ApiKey.StartsWith("AQ.", StringComparison.Ordinal))
        {
            request.Headers.TryAddWithoutValidation("x-goog-api-key", provider.ApiKey);
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", provider.ApiKey);
        }

        var response = await _http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Gemini {Model} returned {Status}: {Body}",
                model, (int)response.StatusCode, errorBody[..Math.Min(errorBody.Length, 500)]);
            response.EnsureSuccessStatusCode(); // re-throw for cascade fallback
        }

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return ParseGeminiResponse(responseBody, providerName, useVision);
    }

    private static AiVerificationResult ParseGeminiResponse(
        string responseBody, string providerName, bool visionUsed)
    {
        using var doc = JsonDocument.Parse(responseBody);

        // Navigate: candidates[0].content.parts[0].text
        var text = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;

        return ParseAiJsonResponse(text, providerName, visionUsed);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Deepseek — OpenAI-compatible REST API, text-only
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<AiVerificationResult> CallDeepseekAsync(
        AiProviderConfig provider,
        Payment payment,
        Booking booking,
        bool imageUploaded,
        string? expectedRecipientName,
        string? expectedRecipientNumber,
        CancellationToken ct)
    {
        var model = provider.Model ?? DefaultDeepseekModel;

        var requestBody = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = "You are a payment verification assistant. Respond ONLY with valid JSON." },
                new { role = "user",   content = BuildVerificationPrompt(payment, booking, expectedRecipientName, expectedRecipientNumber, hasImage: false, imageUploaded: imageUploaded) }
            },
            temperature = 0.1,
            max_tokens  = 512,
            stream      = false
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, DeepseekApiUrl);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return ParseDeepseekResponse(responseBody);
    }

    private static AiVerificationResult ParseDeepseekResponse(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);

        // OpenAI-compatible format: choices[0].message.content
        var text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;

        return ParseAiJsonResponse(text, "Deepseek", false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // OpenRouter — OpenAI-compatible REST API, supports free/commercial vision
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<AiVerificationResult> CallOpenRouterAsync(
        AiProviderConfig provider,
        Payment payment,
        Booking booking,
        string? proofImageSignedUrl,
        string? expectedRecipientName,
        string? expectedRecipientNumber,
        CancellationToken ct)
    {
        var model = provider.Model ?? DefaultOpenRouterModel;
        var useVision = provider.EnableVision && !string.IsNullOrEmpty(proofImageSignedUrl);

        var promptText = BuildVerificationPrompt(
            payment, booking, expectedRecipientName, expectedRecipientNumber, hasImage: useVision, imageUploaded: !string.IsNullOrEmpty(proofImageSignedUrl));

        object userContent;

        if (useVision && proofImageSignedUrl is not null)
        {
            try
            {
                var imageBytes = await _http.GetByteArrayAsync(proofImageSignedUrl, ct);
                var base64     = Convert.ToBase64String(imageBytes);
                var mimeType   = proofImageSignedUrl.Contains(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
                var dataUrl    = $"data:{mimeType};base64,{base64}";

                userContent = new object[]
                {
                    new { type = "text", text = promptText },
                    new { type = "image_url", image_url = new { url = dataUrl } }
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not download proof image for OpenRouter vision. Proceeding text-only.");
                useVision = false;
                userContent = promptText;
            }
        }
        else
        {
            userContent = promptText;
        }

        var requestBody = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = "You are a payment verification assistant. Respond ONLY with valid JSON." },
                new { role = "user",   content = userContent }
            },
            temperature = 0.1,
            max_tokens  = 2048,
            reasoning   = new { max_tokens = 1000 }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, OpenRouterApiUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://punitbola.tech");
        request.Headers.TryAddWithoutValidation("X-Title", "Pikolball Booking");
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("OpenRouter {Model} returned {Status}: {Body}",
                model, (int)response.StatusCode, err[..Math.Min(err.Length, 300)]);
            response.EnsureSuccessStatusCode();
        }

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return ParseOpenRouterResponse(responseBody, useVision);
    }

    private static AiVerificationResult ParseOpenRouterResponse(string responseBody, bool visionUsed)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var msg = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message");

        string? text = null;
        if (msg.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
        {
            text = contentProp.GetString();
        }

        // If content is empty (common in reasoning models if truncated), look for JSON block in reasoning
        if (string.IsNullOrWhiteSpace(text) && msg.TryGetProperty("reasoning", out var reasoningProp))
        {
            var reasoning = reasoningProp.GetString() ?? string.Empty;
            var match = Regex.Match(reasoning, @"\{[^{}]*""confidence""[^{}]*\}");
            if (match.Success)
            {
                text = match.Value;
            }
        }

        return ParseAiJsonResponse(text ?? string.Empty, "OpenRouter", visionUsed);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Shared prompt + response parser
    // ─────────────────────────────────────────────────────────────────────────

    private static string BuildVerificationPrompt(
        Payment payment,
        Booking booking,
        string? expectedRecipientName,
        string? expectedRecipientNumber,
        bool hasImage,
        bool imageUploaded = false)
    {
        string instructionsText;
        string imageStatusText;

        var recipientDetails = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(expectedRecipientName))
        {
            recipientDetails.AppendLine($"- Expected Recipient Admin Name: {expectedRecipientName}");
        }
        if (!string.IsNullOrWhiteSpace(expectedRecipientNumber))
        {
            recipientDetails.AppendLine($"- Expected Recipient Account/Mobile: {expectedRecipientNumber}");
        }
        if (recipientDetails.Length == 0)
        {
            recipientDetails.AppendLine("- Expected Recipient: Authorized Court Administrator");
        }

        if (hasImage)
        {
            imageStatusText = "A payment receipt screenshot is attached. You MUST inspect the receipt image carefully.";
            instructionsText = $"""
                INSTRUCTIONS FOR RECEIPT VERIFICATION:
                1. EXTRACT REFERENCE NUMBER: Look for the transaction / reference number in the screenshot (e.g. GCash Ref No). Does it match '{payment.ReferenceNumber}'? Set reference_matches = true ONLY if they match.
                2. EXTRACT AMOUNT PAID: Look for the peso amount paid in the screenshot. Does it match the expected fee of ₱{booking.Price:F2}? If the receipt shows ANY amount lower than ₱{booking.Price:F2} or a different amount, amount_matches MUST BE FALSE!
                3. EXTRACT RECIPIENT / GCash ADMIN: Look for the recipient name or mobile number (e.g. 'Sent to: ...'). Was this payment sent to {expectedRecipientName ?? "the court admin"}? NOTE: GCash routinely masks recipient names and mobile numbers on receipts with asterisks or bullets (e.g. 'Christle Jude Ayop' appears as 'CH•••E JU•E A.', and mobile '09060672167' appears as '+63 9•• ••• 2167'). If the visible unmasked letters, initials, or ending mobile digits match the expected recipient, recipient_matches MUST BE TRUE! Only mark recipient_matches = false if the payment was sent to an entirely different name or number.
                4. AUTHENTICITY: Check if the screenshot looks like a genuine, unedited payment confirmation receipt. If it is cropped, illegible, or not a receipt, looks_legitimate MUST BE FALSE.
                """;
        }
        else if (imageUploaded)
        {
            imageStatusText = "A payment receipt screenshot was uploaded, but direct image vision is currently unavailable.";
            instructionsText = "INSTRUCTIONS: Without vision, verify reference format only. Confidence must be 0.50 or lower.";
        }
        else
        {
            imageStatusText = "No screenshot was uploaded.";
            instructionsText = "INSTRUCTIONS: Without an image, evaluate reference format alone. Confidence must be 0.50 or lower.";
        }

        // Use string concatenation for the JSON template block to avoid raw string literal brace conflicts
        var jsonTemplate = """
            {
              "extracted_reference": "reference number visible in receipt, or null",
              "extracted_amount": 0.00,
              "extracted_recipient": "recipient name/number visible in receipt, or null",
              "reference_matches": true,
              "amount_matches": true,
              "recipient_matches": true,
              "looks_legitimate": true,
              "confidence": 0.95,
              "reason": "Clear explanation of reference, amount, and recipient checks"
            }
            """;

        return $"""
            You are a strict payment verification auditor for a pickleball court booking facility in the Philippines.
            {imageStatusText}

            EXPECTED BOOKING DETAILS:
            - Booking Reference: {booking.BookingReference}
            - Customer Name: {booking.CustomerName}
            - Court: {booking.Court?.Name ?? "N/A"}
            - Date: {booking.BookingDate:MMMM d, yyyy}
            - Time: {FormatTime(booking.StartTime)} to {FormatTime(booking.EndTime)}
            - Expected Booking Fee: ₱{booking.Price:F2}
            - Payment Method: {payment.PaymentMethod}
            {recipientDetails.ToString().TrimEnd()}

            CUSTOMER SUBMISSION:
            - Submitted Reference Number: {payment.ReferenceNumber}
            - Expected Payment Fee: ₱{payment.Amount:F2}

            {instructionsText}

            CRITICAL SAFETY RULES:
            - If the amount paid on the receipt does NOT match ₱{booking.Price:F2}, amount_matches MUST be false, and confidence MUST be 0.10 or lower!
            - If the recipient on the receipt does NOT match the expected admin/account, recipient_matches MUST be false, and confidence MUST be 0.10 or lower!
            - If the reference number does NOT match, reference_matches MUST be false, and confidence MUST be 0.10 or lower!
            - Set confidence >= 0.85 ONLY if reference_matches, amount_matches, recipient_matches, and looks_legitimate are ALL TRUE!

            YOU MUST RESPOND WITH VALID JSON ONLY — no markdown, no explanation outside the JSON:
            {jsonTemplate}
            """;
    }

    private static AiVerificationResult ParseAiJsonResponse(
        string text, string providerName, bool visionUsed)
    {
        // Strip markdown code fences if the model wrapped the JSON
        var clean = Regex.Replace(text.Trim(), @"^```json?\s*|```$", string.Empty,
            RegexOptions.Multiline).Trim();

        try
        {
            using var doc = JsonDocument.Parse(clean);
            var root = doc.RootElement;

            var confidence       = root.TryGetProperty("confidence", out var c) ? c.GetDouble() : 0.5;
            var reason           = root.TryGetProperty("reason", out var r) ? r.GetString() ?? string.Empty : string.Empty;
            var refMatches       = !root.TryGetProperty("reference_matches", out var rm) || rm.GetBoolean();
            var amountMatches    = !root.TryGetProperty("amount_matches", out var am) || am.GetBoolean();
            var recipientMatches = !root.TryGetProperty("recipient_matches", out var rcm) || rcm.GetBoolean();
            var looksLegitimate  = !root.TryGetProperty("looks_legitimate", out var ll) || ll.GetBoolean();

            // CRITICAL AUDIT GATE:
            // If reference, amount, recipient, or legitimacy checks failed:
            if (!refMatches || !amountMatches || !recipientMatches || !looksLegitimate)
            {
                var failureReasons = new List<string>();
                if (!refMatches)       failureReasons.Add("reference number mismatch");
                if (!amountMatches)    failureReasons.Add("amount mismatch");
                if (!recipientMatches) failureReasons.Add("GCash admin recipient mismatch");
                if (!looksLegitimate)  failureReasons.Add("receipt does not appear legitimate");

                confidence = Math.Min(confidence, 0.10);
                var fullReason = $"Verification FAILED ({string.Join(", ", failureReasons)}). {reason}".Trim();
                return ManualReview(confidence, fullReason, providerName);
            }

            // If vision was not used, we can NEVER auto-verify — cap at 0.50 (ManualReview)
            if (!visionUsed)
            {
                confidence = Math.Min(confidence, 0.50);
                return ManualReview(confidence,
                    $"Vision analysis unavailable. {reason}".Trim(),
                    providerName);
            }

            var decision = confidence >= 0.85
                ? AiVerificationDecision.AutoVerify
                : AiVerificationDecision.ManualReview;

            return new AiVerificationResult(decision, confidence, reason, providerName, visionUsed);
        }
        catch
        {
            // Could not parse JSON — treat as low confidence
            return ManualReview(0.20,
                $"AI response could not be parsed. Raw: {text[..Math.Min(200, text.Length)]}",
                providerName);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static AiVerificationResult ManualReview(double confidence, string reason, string provider)
        => new(AiVerificationDecision.ManualReview, confidence, reason, provider, false);

    private static string FormatTime(TimeSpan t)
    {
        var dt = DateTime.Today.Add(t == TimeSpan.Zero ? TimeSpan.FromHours(24) : t);
        return dt.ToString("h:mm tt");
    }
}
