namespace PickleBallBooking.Services;

/// <summary>
/// Configuration for Supabase Storage, bound from the "Supabase:Storage" section
/// of appsettings.json / user secrets.
/// </summary>
public class SupabaseStorageOptions
{
    public const string SectionName = "Supabase:Storage";

    /// <summary>The Supabase Storage bucket name for court images.</summary>
    public string Bucket { get; set; } = "court-images";

    /// <summary>
    /// Maximum allowed size in bytes for a court image upload.
    /// Default: 3,145,728 bytes (exactly 3 MB).
    /// A file of exactly this size is accepted; 3,145,729 or more is rejected.
    /// </summary>
    public long MaxCourtImageSizeBytes { get; set; } = 3_145_728;
}
