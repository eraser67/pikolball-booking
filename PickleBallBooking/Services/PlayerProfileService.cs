using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

/// <summary>
/// Phase 32: manages player profile creation, updates, and avatar uploads.
///
/// PlayerProfile is a GLOBAL (platform-level) entity with no OrganizationId.
/// The tenant write guard ignores PlayerProfile because it is not in TenantOwnedTypes.
/// </summary>
public sealed class PlayerProfileService
{
    private readonly ApplicationDbContext _context;
    private readonly ICourtImageStorage _storage;
    private readonly ILogger<PlayerProfileService> _logger;

    public PlayerProfileService(
        ApplicationDbContext context,
        ICourtImageStorage storage,
        ILogger<PlayerProfileService> logger)
    {
        _context = context;
        _storage = storage;
        _logger  = logger;
    }

    /// <summary>Returns the player profile for the given Identity user id, or null.</summary>
    public async Task<PlayerProfile?> GetByUserIdAsync(string userId)
        => await _context.PlayerProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId);

    /// <summary>
    /// Creates or updates the player profile.
    /// Idempotent: safe to call on every profile-edit form submission.
    /// </summary>
    public async Task<PlayerProfile> UpsertAsync(
        string userId,
        string firstName,
        string lastName,
        string? displayName,
        string? mobile,
        PlayerSkillLevel skillLevel,
        PlayingHand playingHand,
        string? bio,
        string? location,
        bool isDiscoverable,
        bool privacyMobile,
        MatchHistoryPrivacyLevel privacyMatchHistory = MatchHistoryPrivacyLevel.Public)
    {
        var profile = await _context.PlayerProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile is null)
        {
            profile = new PlayerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };
            _context.PlayerProfiles.Add(profile);
        }

        profile.FirstName            = firstName.Trim();
        profile.LastName             = lastName.Trim();
        profile.DisplayName          = string.IsNullOrWhiteSpace(displayName)
                                        ? $"{firstName.Trim()} {lastName.Trim()}"
                                        : displayName.Trim();
        profile.Mobile               = mobile?.Trim();
        profile.SkillLevel           = skillLevel;
        profile.PlayingHand          = playingHand;
        profile.Bio                  = bio?.Trim();
        profile.Location             = location?.Trim();
        profile.IsDiscoverable       = isDiscoverable;
        profile.PrivacyMobile        = privacyMobile;
        profile.PrivacyMatchHistory  = privacyMatchHistory;
        profile.UpdatedAt            = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Player profile upserted for UserId={UserId}.", userId);
        return profile;
    }

    /// <summary>
    /// Uploads a player avatar to Supabase Storage, persists the path, and
    /// returns the public URL of the uploaded image.
    ///
    /// The old avatar is deleted when the extension changes (e.g. PNG → JPG)
    /// to avoid orphaned files. Same-extension re-uploads are upserted in-place.
    /// </summary>
    public async Task<string> UploadAvatarAsync(
        string userId, IFormFile file, CancellationToken ct = default)
    {
        var profile = await _context.PlayerProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? throw new InvalidOperationException(
                "Player profile not found. Please save your profile before uploading an avatar.");

        var oldPath = profile.AvatarPath;

        // Storage path is built server-side in ICourtImageStorage.
        var newPath = await _storage.UploadAvatarAsync(userId, file, ct);

        // Delete the old file if the extension changed (e.g. user switched from .png to .jpg).
        if (!string.IsNullOrEmpty(oldPath) && oldPath != newPath)
        {
            try   { await _storage.DeleteAsync(oldPath, ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to delete old avatar {OldPath} for UserId={UserId}. " +
                    "File may remain in Supabase Storage.", oldPath, userId);
            }
        }

        profile.AvatarPath = newPath;
        profile.UpdatedAt  = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Avatar uploaded for UserId={UserId}: {Path}.", userId, newPath);
        return _storage.GetPublicUrl(newPath) ?? string.Empty;
    }
}
