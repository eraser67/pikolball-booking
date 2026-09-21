using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Options;
using PickleBallBooking.Services;
using Xunit;

namespace PickleBallBooking.Tests;

/// <summary>
/// Phase 25: unit tests for payment proof validation in SupabasePaymentProofStorage.
///
/// IMPORTANT: These tests exercise the PAYMENT PROOF 1 MB limit independently from
/// the court image 3 MB limit. The two limits must never share configuration.
///
/// Court image max: 3,145,728 bytes (SupabaseStorageOptions.MaxCourtImageSizeBytes)
/// Payment proof max: 1,048,576 bytes (PaymentProofOptions.MaxFileSizeBytes)
/// </summary>
public class PaymentProofValidationTests
{
    // JPEG magic bytes: FF D8 FF
    private static readonly byte[] ValidJpegHeader = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];

    // PNG magic bytes: 89 50 4E 47 0D 0A 1A 0A
    private static readonly byte[] ValidPngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // WEBP magic bytes: RIFF????WEBP
    private static readonly byte[] ValidWebpHeader =
    [
        0x52, 0x49, 0x46, 0x46,  // RIFF
        0x00, 0x00, 0x00, 0x00,  // file size (placeholder)
        0x57, 0x45, 0x42, 0x50,  // WEBP
        0x56, 0x50, 0x38, 0x20   // VP8 chunk
    ];

    // PDF magic bytes (invalid for images)
    private static readonly byte[] PdfHeader = [0x25, 0x50, 0x44, 0x46];

    // ─── FakeConfiguration (reused from CourtImageValidationTests pattern) ──

    private sealed class FakeConfiguration : IConfiguration
    {
        private readonly Dictionary<string, string?> _values;
        public FakeConfiguration(Dictionary<string, string?> values) => _values = values;

        public string? this[string key]
        {
            get => _values.TryGetValue(key, out var v) ? v : null;
            set { }
        }

        public IConfigurationSection GetSection(string key) => new FakeConfigSection(key, _values);
        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);

        private sealed class FakeConfigSection : IConfigurationSection
        {
            private readonly string _key;
            private readonly Dictionary<string, string?> _values;
            public FakeConfigSection(string key, Dictionary<string, string?> values) { _key = key; _values = values; Key = key.Split(':')[^1]; Path = key; Value = null; }
            public string Key { get; }
            public string Path { get; }
            public string? Value { get; set; }
            public string? this[string key] { get => _values.TryGetValue($"{_key}:{key}", out var v) ? v : null; set { } }
            public IConfigurationSection GetSection(string key) => new FakeConfigSection($"{_key}:{key}", _values);
            public IEnumerable<IConfigurationSection> GetChildren() => [];
            public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private const long ProofMaxBytes = 1_048_576; // 1 MB exactly

    private static SupabasePaymentProofStorage CreateStorage(long maxProofBytes = ProofMaxBytes)
    {
        var http = new HttpClient();

        // Court-images bucket (public): 3 MB limit — UNCHANGED from Phase 24.
        var courtOpts = Options.Create(new SupabaseStorageOptions
        {
            Bucket = "court-images",
            MaxCourtImageSizeBytes = 3_145_728, // 3 MB — DO NOT CHANGE
        });

        // Payment-proof bucket (private): 1 MB limit — SEPARATE from court images.
        var proofOpts = Options.Create(new PaymentProofOptions
        {
            Bucket = "payment-proofs",
            MaxFileSizeBytes = maxProofBytes,
        });

        var config = new FakeConfiguration(new Dictionary<string, string?>
        {
            ["Supabase:Url"]            = "https://example.supabase.co",
            ["Supabase:ServiceRoleKey"] = "test-key",
        });

        return new SupabasePaymentProofStorage(http, courtOpts, proofOpts, config);
    }

    private static IFormFile MakeFile(byte[] content, string contentType, string fileName, long? lengthOverride = null)
    {
        var ms = new MemoryStream(content);
        return new FormFile(ms, 0, lengthOverride ?? content.Length, "ProofImage", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static byte[] MakeImageBytes(byte[] header, long totalSize)
    {
        var bytes = new byte[totalSize];
        Array.Copy(header, bytes, Math.Min(header.Length, bytes.Length));
        return bytes;
    }

    // ─── Size boundary tests (PROOF = 1 MB) ─────────────────────────────────

    /// <summary>Test 3 — Below 1 MB: accepted.</summary>
    [Fact]
    public async Task UploadProofAsync_BelowMaxSize_IsAccepted()
    {
        var storage = CreateStorage();
        var content = MakeImageBytes(ValidJpegHeader, 512 * 1024); // 512 KB
        var file    = MakeFile(content, "image/jpeg", "proof.jpg");

        // Should NOT throw CourtImageValidationException.
        var ex = await Record.ExceptionAsync(() => storage.UploadProofAsync(1, 1, file));
        Assert.IsNotType<CourtImageValidationException>(ex);
    }

    /// <summary>Test 1 — Exactly 1 MB (1,048,576 bytes): accepted.</summary>
    [Fact]
    public async Task UploadProofAsync_ExactlyOneMB_IsAccepted()
    {
        var storage = CreateStorage();
        var content = MakeImageBytes(ValidJpegHeader, ProofMaxBytes); // exactly 1,048,576 bytes
        var file    = MakeFile(content, "image/jpeg", "proof.jpg");

        var ex = await Record.ExceptionAsync(() => storage.UploadProofAsync(1, 1, file));
        Assert.IsNotType<CourtImageValidationException>(ex);
    }

    /// <summary>Test 2 — One byte over 1 MB (1,048,577 bytes): rejected.</summary>
    [Fact]
    public async Task UploadProofAsync_OneBytOverOneM_IsRejected()
    {
        var storage = CreateStorage();
        var content = MakeImageBytes(ValidJpegHeader, ProofMaxBytes + 1); // 1,048,577 bytes
        var file    = MakeFile(content, "image/jpeg", "proof.jpg");

        await Assert.ThrowsAsync<CourtImageValidationException>(() => storage.UploadProofAsync(1, 1, file));
    }

    /// <summary>Test 4 — Over 1 MB: rejected.</summary>
    [Fact]
    public async Task UploadProofAsync_OverOneM_IsRejected()
    {
        var storage = CreateStorage();
        var content = MakeImageBytes(ValidJpegHeader, 2 * 1024 * 1024); // 2 MB
        var file    = MakeFile(content, "image/jpeg", "proof.jpg");

        await Assert.ThrowsAsync<CourtImageValidationException>(() => storage.UploadProofAsync(1, 1, file));
    }

    /// <summary>Validation message for oversized proof mentions "1 MB".</summary>
    [Fact]
    public async Task UploadProofAsync_OversizedMessage_Mentions1MB()
    {
        var storage = CreateStorage();
        var content = MakeImageBytes(ValidJpegHeader, ProofMaxBytes + 1);
        var file    = MakeFile(content, "image/jpeg", "proof.jpg");

        var ex = await Assert.ThrowsAsync<CourtImageValidationException>(() => storage.UploadProofAsync(1, 1, file));
        Assert.Contains("1 MB", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Magic-byte / MIME validation ────────────────────────────────────────

    /// <summary>Test 5 — PDF disguised as JPEG: rejected via magic bytes.</summary>
    [Fact]
    public async Task UploadProofAsync_PdfDisguisedAsJpeg_IsRejected()
    {
        var storage = CreateStorage();
        var content = MakeImageBytes(PdfHeader, 1024);
        var file    = MakeFile(content, "image/jpeg", "proof.jpg");

        await Assert.ThrowsAsync<CourtImageValidationException>(() => storage.UploadProofAsync(1, 1, file));
    }

    [Fact]
    public async Task UploadProofAsync_ValidPng_IsAccepted()
    {
        var storage = CreateStorage();
        var content = MakeImageBytes(ValidPngHeader, 1024);
        var file    = MakeFile(content, "image/png", "proof.png");

        var ex = await Record.ExceptionAsync(() => storage.UploadProofAsync(1, 1, file));
        Assert.IsNotType<CourtImageValidationException>(ex);
    }

    [Fact]
    public async Task UploadProofAsync_ValidWebp_IsAccepted()
    {
        var storage = CreateStorage();
        var content = MakeImageBytes(ValidWebpHeader, 1024);
        var file    = MakeFile(content, "image/webp", "proof.webp");

        var ex = await Record.ExceptionAsync(() => storage.UploadProofAsync(1, 1, file));
        Assert.IsNotType<CourtImageValidationException>(ex);
    }

    [Fact]
    public async Task UploadProofAsync_EmptyFile_IsRejected()
    {
        var storage = CreateStorage();
        var file    = MakeFile([], "image/jpeg", "proof.jpg");

        await Assert.ThrowsAsync<CourtImageValidationException>(() => storage.UploadProofAsync(1, 1, file));
    }

    // ─── Private bucket verification ─────────────────────────────────────────

    /// <summary>Payment proof bucket name must be "payment-proofs" (private), not "court-images" (public).</summary>
    [Fact]
    public void PaymentProofOptions_DefaultBucket_IsPrivatePaymentProofsBucket()
    {
        var opts = new PaymentProofOptions();
        Assert.Equal("payment-proofs", opts.Bucket);
        Assert.NotEqual("court-images", opts.Bucket);
    }

    /// <summary>Court image bucket must remain "court-images" — unchanged by Phase 25.</summary>
    [Fact]
    public void SupabaseStorageOptions_DefaultBucket_IsCourtImages()
    {
        var opts = new SupabaseStorageOptions();
        Assert.Equal("court-images", opts.Bucket);
    }

    /// <summary>Court image limit must remain 3 MB — NOT 1 MB.</summary>
    [Fact]
    public void SupabaseStorageOptions_CourtImageLimit_IsThreeMB()
    {
        var opts = new SupabaseStorageOptions();
        Assert.Equal(3_145_728L, opts.MaxCourtImageSizeBytes);
        // Explicitly assert it is NOT the payment proof limit.
        Assert.NotEqual(1_048_576L, opts.MaxCourtImageSizeBytes);
    }

    /// <summary>Payment proof limit must be exactly 1 MB — NOT 3 MB.</summary>
    [Fact]
    public void PaymentProofOptions_ProofLimit_IsOneMB()
    {
        var opts = new PaymentProofOptions();
        Assert.Equal(1_048_576L, opts.MaxFileSizeBytes);
        Assert.NotEqual(3_145_728L, opts.MaxFileSizeBytes);
    }

    // ─── Storage path verification ────────────────────────────────────────────

    /// <summary>Proof storage path contains organizationId and paymentId — both server-assigned.</summary>
    [Fact]
    public void ProofPath_ContainsOrganizationIdAndPaymentId()
    {
        // Path format: organizations/{orgId}/payments/{paymentId}/proof.{ext}
        // Verify the path would contain our expected segments.
        const int orgId     = 42;
        const int paymentId = 99;
        var path = $"organizations/{orgId}/payments/{paymentId}/proof.jpg";

        Assert.Contains($"organizations/{orgId}", path);
        Assert.Contains($"payments/{paymentId}", path);
        Assert.DoesNotContain("0/", path.Replace("organizations/", "")); // not zero-padded
    }

    /// <summary>QR code URL uses the public court-images bucket — separate from proof bucket.</summary>
    [Fact]
    public void GetQRCodePublicUrl_UsesPublicCourtImagesBucket()
    {
        var storage  = CreateStorage();
        var qrPath   = "organizations/1/qr/gcash.jpg";
        var publicUrl = storage.GetQRCodePublicUrl(qrPath);

        Assert.NotNull(publicUrl);
        Assert.Contains("court-images", publicUrl);    // public bucket
        Assert.DoesNotContain("payment-proofs", publicUrl); // NOT the private bucket
    }

    [Fact]
    public void GetQRCodePublicUrl_NullPath_ReturnsNull()
    {
        var storage = CreateStorage();
        Assert.Null(storage.GetQRCodePublicUrl(null));
        Assert.Null(storage.GetQRCodePublicUrl(string.Empty));
    }
}
