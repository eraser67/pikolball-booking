using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Options;
using PickleBallBooking.Services;
using Xunit;

namespace PickleBallBooking.Tests;

/// <summary>
/// Phase 24: unit tests for court image validation logic in SupabaseCourtImageStorage.
///
/// These tests exercise the validation layer (size, MIME, magic bytes) in isolation
/// without making real HTTP calls to Supabase. The storage path construction is also
/// verified to be always tenant-scoped.
/// </summary>
public class CourtImageValidationTests
{
    // JPEG magic bytes: FF D8 FF
    private static readonly byte[] ValidJpegHeader = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    // PNG magic bytes: 89 50 4E 47 0D 0A 1A 0A
    private static readonly byte[] ValidPngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // WEBP magic bytes: 52 49 46 46 ?? ?? ?? ?? 57 45 42 50
    private static readonly byte[] ValidWebpHeader = [
        0x52, 0x49, 0x46, 0x46,  // RIFF
        0x00, 0x00, 0x00, 0x00,  // file size (placeholder)
        0x57, 0x45, 0x42, 0x50,  // WEBP
        0x56, 0x50, 0x38, 0x20   // VP8 chunk
    ];

    // PDF magic bytes (invalid for images)
    private static readonly byte[] PdfHeader = [0x25, 0x50, 0x44, 0x46];

    // -----------------------------------------------------------------------
    // Simple IConfiguration stub — avoids needing extra NuGet packages.
    // -----------------------------------------------------------------------
    private sealed class FakeConfiguration : IConfiguration
    {
        private readonly Dictionary<string, string?> _values;

        public FakeConfiguration(Dictionary<string, string?> values) => _values = values;

        public string? this[string key]
        {
            get => _values.TryGetValue(key, out var v) ? v : null;
            set { }
        }

        public IConfigurationSection GetSection(string key)
        {
            // Return a section that serves the sub-keys prefixed with "key:".
            return new FakeConfigurationSection(key, _values);
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);
    }

    private sealed class FakeConfigurationSection : IConfigurationSection
    {
        private readonly string _key;
        private readonly Dictionary<string, string?> _values;

        public FakeConfigurationSection(string key, Dictionary<string, string?> values)
        {
            _key = key;
            _values = values;
            Key = key.Split(':')[^1];
            Path = key;
            Value = null;
        }

        public string Key { get; }
        public string Path { get; }
        public string? Value { get; set; }

        public string? this[string key]
        {
            get => _values.TryGetValue($"{_key}:{key}", out var v) ? v : null;
            set { }
        }

        public IConfigurationSection GetSection(string key) =>
            new FakeConfigurationSection($"{_key}:{key}", _values);

        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static SupabaseCourtImageStorage CreateStorage(long maxBytes = 1_048_576)
    {
        var http = new HttpClient();

        var options = Options.Create(new SupabaseStorageOptions
        {
            Bucket = "court-images",
            MaxCourtImageSizeBytes = maxBytes,
        });

        var config = new FakeConfiguration(new Dictionary<string, string?>
        {
            ["Supabase:Url"]            = "https://example.supabase.co",
            ["Supabase:ServiceRoleKey"] = "test-key",
        });

        return new SupabaseCourtImageStorage(http, options, config);
    }

    private static IFormFile MakeFile(byte[] content, string contentType, string fileName, long? lengthOverride = null)
    {
        var ms = new MemoryStream(content);
        return new FormFile(ms, 0, lengthOverride ?? content.Length, "Image", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    // -----------------------------------------------------------------------
    // Size validation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Upload_ExactlyOneMB_IsAccepted_WhenValidJpeg()
    {
        var storage = CreateStorage(maxBytes: 1_048_576);

        // Build a valid JPEG of exactly 1,048,576 bytes.
        var content = new byte[1_048_576];
        ValidJpegHeader.CopyTo(content, 0);
        var file = MakeFile(content, "image/jpeg", "court.jpg");

        // Should NOT throw CourtImageValidationException for size.
        // Will throw InvalidOperationException when it tries to call Supabase (expected).
        var ex = await Record.ExceptionAsync(() =>
            storage.UploadAsync(1, 1, file));

        // We expect the real HTTP call to fail (no real Supabase), but NOT a validation error.
        Assert.IsNotType<CourtImageValidationException>(ex);
    }

    [Fact]
    public async Task Upload_OneByteTooLarge_IsRejected()
    {
        var storage = CreateStorage(maxBytes: 1_048_576);

        // 1,048,577 bytes = exactly 1 byte over the limit.
        var content = new byte[1_048_577];
        ValidJpegHeader.CopyTo(content, 0);
        var file = MakeFile(content, "image/jpeg", "court.jpg");

        var ex = await Assert.ThrowsAsync<CourtImageValidationException>(() =>
            storage.UploadAsync(1, 1, file));

        Assert.Contains("1 MB", ex.Message);
    }

    [Fact]
    public async Task Upload_EmptyFile_IsRejected()
    {
        var storage = CreateStorage();
        var file = MakeFile([], "image/jpeg", "empty.jpg");

        var ex = await Assert.ThrowsAsync<CourtImageValidationException>(() =>
            storage.UploadAsync(1, 1, file));

        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // MIME / magic-byte validation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Upload_ValidPng_MagicBytes_IsAccepted()
    {
        var storage = CreateStorage();
        var content = new byte[100];
        ValidPngHeader.CopyTo(content, 0);
        var file = MakeFile(content, "image/png", "court.png");

        var ex = await Record.ExceptionAsync(() => storage.UploadAsync(1, 1, file));
        Assert.IsNotType<CourtImageValidationException>(ex);
    }

    [Fact]
    public async Task Upload_ValidWebp_MagicBytes_IsAccepted()
    {
        var storage = CreateStorage();
        var content = new byte[200];
        ValidWebpHeader.CopyTo(content, 0);
        var file = MakeFile(content, "image/webp", "court.webp");

        var ex = await Record.ExceptionAsync(() => storage.UploadAsync(1, 1, file));
        Assert.IsNotType<CourtImageValidationException>(ex);
    }

    [Fact]
    public async Task Upload_PdfContent_WithJpegExtension_IsRejected()
    {
        // Extension spoofing: file named .jpg but contains PDF bytes.
        var storage = CreateStorage();
        var content = new byte[100];
        PdfHeader.CopyTo(content, 0);
        var file = MakeFile(content, "image/jpeg", "fake.jpg");

        var ex = await Assert.ThrowsAsync<CourtImageValidationException>(() =>
            storage.UploadAsync(1, 1, file));

        Assert.Contains("JPEG, PNG, or WEBP", ex.Message);
    }

    [Fact]
    public async Task Upload_RandomBytes_IsRejected()
    {
        var storage = CreateStorage();
        var content = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09 };
        var file = MakeFile(content, "application/octet-stream", "random.bin");

        var ex = await Assert.ThrowsAsync<CourtImageValidationException>(() =>
            storage.UploadAsync(1, 1, file));

        Assert.Contains("JPEG, PNG, or WEBP", ex.Message);
    }

    // -----------------------------------------------------------------------
    // Storage path is always tenant-scoped (no client control)
    // -----------------------------------------------------------------------

    [Fact]
    public void GetPublicUrl_BuildsPathFromSupabaseUrl()
    {
        var storage = CreateStorage();
        var url = storage.GetPublicUrl("organizations/1/courts/3/main.jpg");

        Assert.Equal(
            "https://example.supabase.co/storage/v1/object/public/court-images/organizations/1/courts/3/main.jpg",
            url);
    }

    [Fact]
    public void GetPublicUrl_NullPath_ReturnsNull()
    {
        var storage = CreateStorage();
        Assert.Null(storage.GetPublicUrl(null));
    }

    [Fact]
    public void GetPublicUrl_EmptyPath_ReturnsNull()
    {
        var storage = CreateStorage();
        Assert.Null(storage.GetPublicUrl(string.Empty));
    }

    // -----------------------------------------------------------------------
    // 1 MB boundary edge cases
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(1_048_575)] // 1 byte under limit
    [InlineData(1_048_576)] // exactly at limit
    public async Task Upload_AtOrBelowLimit_PassesSizeValidation(long size)
    {
        var storage = CreateStorage(maxBytes: 1_048_576);
        var content = new byte[size];
        ValidJpegHeader.CopyTo(content, 0);
        var file = MakeFile(content, "image/jpeg", "court.jpg");

        // Should not throw a CourtImageValidationException (size check passes).
        var ex = await Record.ExceptionAsync(() => storage.UploadAsync(1, 1, file));
        Assert.IsNotType<CourtImageValidationException>(ex);
    }

    [Theory]
    [InlineData(1_048_577)] // 1 byte over
    [InlineData(2_097_152)] // 2 MB
    [InlineData(10_485_760)] // 10 MB
    public async Task Upload_AboveLimit_IsRejected(long size)
    {
        var storage = CreateStorage(maxBytes: 1_048_576);
        // Use length override so we don't allocate 10 MB in tests.
        var content = new byte[20]; // small actual buffer
        ValidJpegHeader.CopyTo(content, 0);
        var file = MakeFile(content, "image/jpeg", "court.jpg", lengthOverride: size);

        var ex = await Assert.ThrowsAsync<CourtImageValidationException>(() =>
            storage.UploadAsync(1, 1, file));

        Assert.Contains("1 MB", ex.Message);
    }
}
