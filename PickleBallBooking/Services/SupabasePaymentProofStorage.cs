using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PickleBallBooking.Services;

/// <summary>Phase 25: storage for payment proof screenshots and GCash QR codes.</summary>
public interface IPaymentProofStorage
{
    /// <summary>
    /// Uploads a customer's payment proof screenshot to the PRIVATE payment-proofs bucket.
    /// Returns the Supabase Storage path (not a URL — access requires a signed URL).
    ///
    /// Path: organizations/{organizationId}/payments/{paymentId}/proof.{ext}
    ///
    /// SECURITY:
    /// - organizationId comes from the server-resolved tenant, NEVER from client input.
    /// - paymentId comes from the server-loaded Payment record, NEVER from client input.
    /// - No path traversal: neither parameter is a string the client can control.
    /// </summary>
    Task<string> UploadProofAsync(int organizationId, int paymentId, IFormFile file, CancellationToken ct = default);

    /// <summary>
    /// Uploads an organization's GCash QR code image to the PUBLIC court-images bucket.
    /// Returns the Supabase Storage path. Public URL resolved via GetQRCodePublicUrl.
    ///
    /// Path: organizations/{organizationId}/qr/gcash.{ext}
    ///
    /// QR images are intentionally public so customers can scan them on the payment page.
    /// This is a separate concern from payment proof (which is private).
    /// </summary>
    Task<string> UploadQRCodeAsync(int organizationId, IFormFile file, CancellationToken ct = default);

    /// <summary>Returns the public URL for a QR code image path (court-images bucket).</summary>
    string? GetQRCodePublicUrl(string? imagePath);

    /// <summary>
    /// Returns a short-lived signed URL for a payment proof image.
    /// Admin-only: proof images live in the private payment-proofs bucket and are
    /// never directly accessible. The signed URL expires after expiresInSeconds.
    /// Returns null if the path is null/empty or the request fails.
    /// </summary>
    Task<string?> GetProofSignedUrlAsync(string? imagePath, int expiresInSeconds = 300, CancellationToken ct = default);

    /// <summary>
    /// Deletes a proof file from the private payment-proofs bucket.
    /// Safe to call with null/empty (no-op).
    /// </summary>
    Task DeleteProofAsync(string? imagePath, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class SupabasePaymentProofStorage : IPaymentProofStorage
{
    private readonly HttpClient _http;

    // Court-images bucket (PUBLIC) — used for QR codes only.
    private readonly SupabaseStorageOptions _courtOpts;

    // Payment-proof bucket (PRIVATE) — used for proof screenshots only.
    // IMPORTANT: MaxFileSizeBytes here is 1 MB. Do NOT confuse with court image 3 MB limit.
    private readonly PaymentProofOptions _proofOpts;

    private readonly string _supabaseUrl;
    private readonly string _serviceRoleKey;
    private readonly ILogger<SupabasePaymentProofStorage> _logger;

    // Magic-byte signatures for file-header validation (prevents extension/MIME spoofing).
    private static readonly (byte[] Header, string Mime)[] MagicBytes =
    [
        (new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg"),
        (new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png"),
        (new byte[] { 0x52, 0x49, 0x46, 0x46 }, "image/webp"),
    ];

    private static readonly Dictionary<string, string> MimeToExtension = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"]  = ".png",
        ["image/webp"] = ".webp",
    };

    public SupabasePaymentProofStorage(
        HttpClient http,
        IOptions<SupabaseStorageOptions> courtOpts,
        IOptions<PaymentProofOptions> proofOpts,
        IConfiguration config,
        ILogger<SupabasePaymentProofStorage> logger)
    {
        _http           = http;
        _courtOpts      = courtOpts.Value;
        _proofOpts      = proofOpts.Value;
        _supabaseUrl    = (config["Supabase:Url"] ?? string.Empty).TrimEnd('/');
        _serviceRoleKey = config["Supabase:ServiceRoleKey"] ?? string.Empty;
        _logger         = logger;
    }

    // ─── PROOF (PRIVATE bucket: payment-proofs) ──────────────────────────────

    public async Task<string> UploadProofAsync(
        int organizationId,
        int paymentId,
        IFormFile file,
        CancellationToken ct = default)
    {
        // Validates size against 1 MB limit (PaymentProofOptions.MaxFileSizeBytes).
        var (bytes, mime) = await ValidateAndReadAsync(file, ct);
        var ext  = MimeToExtension[mime];

        // Path is 100% server-constructed. Client cannot influence organizationId or paymentId.
        var path = $"organizations/{organizationId}/payments/{paymentId}/proof{ext}";
        await UploadToSupabaseAsync(_proofOpts.Bucket, path, bytes, mime, ct);
        return path;
    }

    public async Task<string?> GetProofSignedUrlAsync(
        string? imagePath,
        int expiresInSeconds = 300,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;

        // POST to Supabase Storage sign endpoint.
        // The service_role key is used SERVER-SIDE only — never sent to the browser.
        var url     = $"{_supabaseUrl}/storage/v1/object/sign/{_proofOpts.Bucket}/{imagePath}";
        var payload = JsonSerializer.Serialize(new { expiresIn = expiresInSeconds });

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        AttachAuth(req);

        var response = await _http.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode) return null;

        var body = await response.Content.ReadAsStringAsync(ct);

        // Log the raw body so we can verify what Supabase returns during development.
        _logger.LogDebug("Supabase sign response for {Path}: {Body}", imagePath, body);

        // Use JsonDocument for robust parsing.
        // Supabase has used both "signedURL" (older) and "signedUrl" (newer) in different SDK versions.
        // JsonDocument also correctly unescapes any \/ sequences in the URL.
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? signedPath = null;

            if (root.TryGetProperty("signedURL", out var prop1) && prop1.ValueKind == JsonValueKind.String)
                signedPath = prop1.GetString();
            else if (root.TryGetProperty("signedUrl", out var prop2) && prop2.ValueKind == JsonValueKind.String)
                signedPath = prop2.GetString();

            if (string.IsNullOrWhiteSpace(signedPath)) return null;

            // Supabase returns "/object/sign/..." but the correct URL needs "/storage/v1/object/sign/..."
            // Normalize the path to always include the /storage/v1 prefix.
            if (!signedPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                if (!signedPath.StartsWith("/storage/", StringComparison.OrdinalIgnoreCase))
                    signedPath = "/storage/v1" + signedPath;
                signedPath = _supabaseUrl + signedPath;
            }

            return signedPath;
        }
        catch
        {
            return null;
        }
    }

    public async Task DeleteProofAsync(string? imagePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return;

        var url     = $"{_supabaseUrl}/storage/v1/object/{_proofOpts.Bucket}";
        var payload = JsonSerializer.Serialize(new { prefixes = new[] { imagePath } });

        using var req = new HttpRequestMessage(HttpMethod.Delete, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        AttachAuth(req);
        await _http.SendAsync(req, ct);
    }

    // ─── QR CODE (PUBLIC bucket: court-images) ───────────────────────────────

    public async Task<string> UploadQRCodeAsync(
        int organizationId,
        IFormFile file,
        CancellationToken ct = default)
    {
        // QR code uploads use the same 1 MB limit as proof (conservative — QR images are small).
        var (bytes, mime) = await ValidateAndReadAsync(file, ct);
        var ext  = MimeToExtension[mime];
        var path = $"organizations/{organizationId}/qr/gcash{ext}";

        // QR codes go into the PUBLIC court-images bucket so customers can view them.
        await UploadToSupabaseAsync(_courtOpts.Bucket, path, bytes, mime, ct);
        return path;
    }

    public string? GetQRCodePublicUrl(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;
        return $"{_supabaseUrl}/storage/v1/object/public/{_courtOpts.Bucket}/{imagePath}";
    }

    // ─── Shared validation ────────────────────────────────────────────────────

    /// <summary>
    /// Validates the file and reads its bytes.
    /// Uses PaymentProofOptions.MaxFileSizeBytes (1 MB) — independent from court-image limit.
    /// </summary>
    private async Task<(byte[] Bytes, string Mime)> ValidateAndReadAsync(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new CourtImageValidationException("No file was provided or the file is empty.");

        // AUTHORITATIVE server-side size check (1 MB).
        if (file.Length > _proofOpts.MaxFileSizeBytes)
            throw new CourtImageValidationException(
                $"Payment proof must be 1 MB or smaller " +
                $"(max {_proofOpts.MaxFileSizeBytes:N0} bytes). " +
                $"The uploaded file is {file.Length:N0} bytes.");

        using var ms = new MemoryStream((int)file.Length);
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        // Magic-byte validation — prevents MIME/extension spoofing.
        var mime = DetectMime(bytes)
            ?? throw new CourtImageValidationException(
                "File content does not match a supported image format (JPEG, PNG, or WEBP). " +
                "Renaming a non-image file is not permitted.");

        return (bytes, mime);
    }

    private static string? DetectMime(byte[] bytes)
    {
        foreach (var (header, mime) in MagicBytes)
        {
            if (bytes.Length < header.Length) continue;
            if (!bytes.Take(header.Length).SequenceEqual(header)) continue;

            // WEBP requires an additional 4-byte "WEBP" marker at offset 8.
            if (mime == "image/webp")
            {
                if (bytes.Length < 12) continue;
                var webp = new byte[] { 0x57, 0x45, 0x42, 0x50 };
                if (!bytes.Skip(8).Take(4).SequenceEqual(webp)) continue;
            }

            return mime;
        }
        return null;
    }

    private async Task UploadToSupabaseAsync(string bucket, string path, byte[] bytes, string contentType, CancellationToken ct)
    {
        var url = $"{_supabaseUrl}/storage/v1/object/{bucket}/{path}";
        using var req = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new ByteArrayContent(bytes)
        };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        req.Headers.Add("x-upsert", "true");
        AttachAuth(req);

        var response = await _http.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Supabase Storage upload failed ({response.StatusCode}): {body}");
        }
    }

    private void AttachAuth(HttpRequestMessage req)
    {
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serviceRoleKey);
        req.Headers.Add("apikey", _serviceRoleKey);
    }
}
