using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

/// <summary>
/// Phase 32: manages player profile creation and updates.
///
/// PlayerProfile is a GLOBAL (platform-level) entity with no OrganizationId.
/// All reads and writes use IgnoreQueryFilters so they are never blocked by
/// the tenant global query filter — even when called from a tenant-resolved
/// request context.
///
/// SaveChanges on PlayerProfile does NOT trigger the tenant write guard because
/// PlayerProfile is not in the TenantOwnedTypes set.
/// </summary>
public sealed class PlayerProfileService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<PlayerProfileService> _logger;

    public PlayerProfileService(ApplicationDbContext context, ILogger<PlayerProfileService> logger)
    {
        _context = context;
        _logger  = logger;
    }

    /// <summary>
    /// Returns the player profile for the given Identity user id, or null if
    /// the player has not yet completed their profile.
    /// </summary>
    public async Task<PlayerProfile?> GetByUserIdAsync(string userId)
        => await _context.PlayerProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId);

    /// <summary>
    /// Creates or updates the player profile for the given user.
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
        bool privacyMobile)
    {
        var profile = await _context.PlayerProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile is null)
        {
            profile = new PlayerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };
            _context.PlayerProfiles.Add(profile);
        }

        profile.FirstName      = firstName.Trim();
        profile.LastName       = lastName.Trim();
        profile.DisplayName    = string.IsNullOrWhiteSpace(displayName)
                                    ? $"{firstName.Trim()} {lastName.Trim()}"
                                    : displayName.Trim();
        profile.Mobile         = mobile?.Trim();
        profile.SkillLevel     = skillLevel;
        profile.PlayingHand    = playingHand;
        profile.Bio            = bio?.Trim();
        profile.Location       = location?.Trim();
        profile.IsDiscoverable = isDiscoverable;
        profile.PrivacyMobile  = privacyMobile;
        profile.UpdatedAt      = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Player profile upserted for UserId={UserId}.", userId);
        return profile;
    }
}
