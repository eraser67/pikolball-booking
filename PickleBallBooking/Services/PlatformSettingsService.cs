using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

/// <summary>
/// Reads and writes the single-row PlatformSettings record.
/// Exposes a cached Get and an Update for the platform admin settings page.
/// </summary>
public class PlatformSettingsService
{
    private readonly ApplicationDbContext _context;

    public PlatformSettingsService(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>Returns the current platform settings, creating the default row if it does not exist.</summary>
    public async Task<PlatformSettings> GetAsync(CancellationToken ct = default)
    {
        var settings = await _context.PlatformSettings.FirstOrDefaultAsync(ct);
        if (settings is not null) return settings;

        // Bootstrap the row on first access.
        settings = new PlatformSettings
        {
            Id        = 1,
            UpdatedAt = DateTime.UtcNow
        };
        _context.PlatformSettings.Add(settings);
        await _context.SaveChangesAsync(ct);
        return settings;
    }

    /// <summary>Updates the platform settings record.</summary>
    public async Task UpdateAsync(
        bool allowTenantRegistration,
        string? registrationMessage,
        CancellationToken ct = default)
    {
        var settings = await GetAsync(ct);
        settings.AllowTenantRegistration = allowTenantRegistration;
        settings.RegistrationMessage     = string.IsNullOrWhiteSpace(registrationMessage)
            ? null : registrationMessage.Trim();
        settings.UpdatedAt               = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
    }
}
