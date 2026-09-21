using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace PickleBallBooking.Services;

/// <summary>
/// Supabase Storage implementation of <see cref="ICourtImageStorage"/>.
///
/// Uses the Supabase Storage REST API directly via HttpClient — no Supabase SDK needed.
///
/// API endpoints:
///   Upload: PUT /storage/v1/object/{bucket}/{path}
///   Delete: DELETE /storage/v1/object/{bucket}  body: {"prefixes": [...]}
///   Public URL: {supabaseUrl}/storage/v1/object/public/{bucket}/{path}
///
/// The service_role JWT is used server-side only; it is never exposed to clients.
/// </summary>
public class SupabaseCourtImageStorage : ICourtImageStorage
{
    // Allowed MIME types for court images.
    private static readonly Dictionary<string, string[]> AllowedMimeExtensions = new()
    {
        ["image/jpeg"] = [".jpg", ".jpeg"],
        ["image/png"]  = [".png"],
        ["image/webp"] = [".webp"],
    };

    // Magic byte signatures for file-header validation (prevents extension spoofing).
    private static readonly (byte[] Header, string Mime)[] MagicBytes =
    [
        (new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg"),
        (new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png"),
        (new byte[] { 0x52, 0x49, 0x46, 0x46 }, "image/webp"),   // RIFF…WEBP checked below
    ];

    private readonly HttpClient _http;
    private readonly SupabaseStorageOptions _options;
    private readonly string _supabaseUrl;
    private readonly string _serviceRoleKey;

    public SupabaseCourtImageStorage(
        HttpClient http,
        IOptions<SupabaseStorageOptions> options,
        IConfiguration configuration)
    {
        _http           = http;
        _options        = options.Value;
        _supabaseUrl    = (configuration["Supabase:Url"] ?? string.Empty).TrimEnd('/');
        _serviceRoleKey = configuration["Supabase:ServiceRoleKey"] ?? string.Empty;
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    public async Task<string> UploadAsync(
        int organizationId, int courtId, IFormFile file, CancellationToken ct = default)
    {
        // 1. Server-side size validation (exact 1 MB boundary check).
        if (file.Length > _options.MaxCourtImageSizeBytes)
        {
            throw new CourtImageValidationException(
                $"Court image must be 1 MB or smaller (max {_options.MaxCourtImageSizeBytes:N0} bytes). " +
                $"The uploaded file is {file.Length:N0} bytes.");
        }

        if (file.Length == 0)
        {
            throw new CourtImageValidationException("Court image file is empty.");
        }

        // 2. Read the file into memory (within limit, so safe).
        using var ms = new MemoryStream((int)file.Length);
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        // 3. Validate magic bytes (prevents extension spoofing).
        var detectedMime = DetectMimeFromBytes(bytes)
            ?? throw new CourtImageValidationException(
                "Court image must be a valid JPEG, PNG, or WEBP file. " +
                "The uploaded file does not match a supported image format.");

        // 4. Validate content-type header (belt-and-suspenders with magic-byte check).
        var contentType = file.ContentType?.ToLowerInvariant() ?? string.Empty;
        if (!AllowedMimeExtensions.ContainsKey(contentType))
        {
            // Still allow if magic bytes detected a valid type (e.g. browser sent wrong header).
            contentType = detectedMime;
        }

        // Map MIME to a canonical extension.
        var extension = AllowedMimeExtensions[detectedMime][0]; // e.g. ".jpg"

        // 5. Build storage path server-side — client cannot influence this.
        var storagePath = BuildStoragePath(organizationId, courtId, extension);

        // 6. Upload via Supabase Storage REST API.
        await UploadToSupabaseAsync(storagePath, bytes, detectedMime, ct);

        return storagePath;
    }

    public async Task DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(storagePath)) return;

        // DELETE /storage/v1/object/{bucket}  body: {"prefixes": ["path"]}
        var url     = $"{_supabaseUrl}/storage/v1/object/{_options.Bucket}";
        var payload = JsonSerializer.Serialize(new { prefixes = new[] { storagePath } });

        using var request = new HttpRequestMessage(HttpMethod.Delete, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        AttachAuth(request);

        var response = await _http.SendAsync(request, ct);
        // A 404 from Supabase on delete is acceptable (object already gone).
        if (!response.IsSuccessStatusCode && (int)response.StatusCode != 404)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Supabase Storage delete failed ({response.StatusCode}): {body}");
        }
    }

    public string? GetPublicUrl(string? storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath)) return null;
        return $"{_supabaseUrl}/storage/v1/object/public/{_options.Bucket}/{storagePath}";
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private static string BuildStoragePath(int organizationId, int courtId, string extension)
        => $"organizations/{organizationId}/courts/{courtId}/main{extension}";

    private static string? DetectMimeFromBytes(byte[] bytes)
    {
        if (bytes.Length < 4) return null;

        foreach (var (header, mime) in MagicBytes)
        {
            if (bytes.Length < header.Length) continue;
            if (!bytes.Take(header.Length).SequenceEqual(header)) continue;

            // WEBP requires additional check: bytes[8..11] == "WEBP"
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

    private async Task UploadToSupabaseAsync(
        string storagePath, byte[] bytes, string contentType, CancellationToken ct)
    {
        // PUT /storage/v1/object/{bucket}/{path}
        // upsert=true replaces any existing object at that path.
        var url = $"{_supabaseUrl}/storage/v1/object/{_options.Bucket}/{storagePath}";

        using var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        request.Headers.Add("x-upsert", "true");
        AttachAuth(request);

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Supabase Storage upload failed ({response.StatusCode}): {body}");
        }
    }

    private void AttachAuth(HttpRequestMessage request)
    {
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _serviceRoleKey);
        request.Headers.Add("apikey", _serviceRoleKey);
    }
}
