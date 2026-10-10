namespace PickleBallBooking.Services;

/// <summary>
/// Phase 25: configuration for payment proof storage.
/// Bound from the "PaymentProof" section of appsettings.json / user secrets.
///
/// IMPORTANT: These limits are INDEPENDENT from SupabaseStorageOptions (court images).
/// - Court images: 3 MB (MaxCourtImageSizeBytes in SupabaseStorageOptions)
/// - Payment proofs: 3 MB (MaxFileSizeBytes here)
/// Do NOT merge or share these configurations.
/// </summary>
public class PaymentProofOptions
{
    public const string SectionName = "PaymentProof";

    /// <summary>
    /// Name of the PRIVATE Supabase Storage bucket that holds payment proof screenshots.
    /// This must be a separate, private bucket — NOT the public court-images bucket.
    /// </summary>
    public string Bucket { get; set; } = "payment-proofs";

    /// <summary>
    /// Maximum allowed size in bytes for a customer payment proof image.
    /// Default: 3,145,728 bytes (exactly 3 MB).
    /// A file of exactly this size is accepted; 3,145,729 or more is rejected.
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 3_145_728;
}
