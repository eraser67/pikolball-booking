using Microsoft.AspNetCore.Http;

namespace PickleBallBooking.Services;

/// <summary>
/// Abstraction over Supabase Storage for court image operations.
/// All methods enforce that the storage path is constructed server-side
/// from the resolved OrganizationId + server-loaded CourtId.
/// </summary>
public interface ICourtImageStorage
{
    /// <summary>
    /// Validates and uploads a court image to Supabase Storage.
    /// The storage path is built server-side: organizations/{orgId}/courts/{courtId}/main.{ext}
    /// The client cannot influence the path.
    /// </summary>
    /// <param name="organizationId">Resolved tenant organization ID.</param>
    /// <param name="courtId">Server-loaded court ID belonging to that tenant.</param>
    /// <param name="file">The uploaded file. Validated for MIME type and size.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The storage path that was written (to be persisted in Court.ImagePath).</returns>
    /// <exception cref="CourtImageValidationException">
    /// Thrown when file type or size validation fails. The existing court image remains unchanged.
    /// </exception>
    Task<string> UploadAsync(int organizationId, int courtId, IFormFile file, CancellationToken ct = default);

    /// <summary>
    /// Validates and uploads an organization's custom brand logo to Supabase Storage (court-images public bucket).
    /// The storage path is built server-side: organizations/{orgId}/logo/logo{ext}
    /// Max size: 3 MB.
    /// </summary>
    Task<string> UploadLogoAsync(int organizationId, IFormFile file, CancellationToken ct = default);

    /// <summary>
    /// Validates and uploads an organization's custom hero image to Supabase Storage (court-images public bucket).
    /// The storage path is built server-side: organizations/{orgId}/hero/hero{ext}
    /// Max size: 3 MB. Displayed on the public landing page instead of the default hero artwork.
    /// </summary>
    Task<string> UploadHeroImageAsync(int organizationId, IFormFile file, CancellationToken ct = default);

    /// <summary>
    /// Deletes a stored court image using the server-side stored path.
    /// The caller must load the path from the database; the client cannot supply an arbitrary path.
    /// </summary>
    Task DeleteAsync(string storagePath, CancellationToken ct = default);

    /// <summary>
    /// Returns the public URL for a stored court image path.
    /// Returns null when storagePath is null or empty.
    /// </summary>
    string? GetPublicUrl(string? storagePath);
}

/// <summary>
/// Thrown when a court image file fails server-side validation.
/// </summary>
public class CourtImageValidationException : Exception
{
    public CourtImageValidationException(string message) : base(message) { }
}
