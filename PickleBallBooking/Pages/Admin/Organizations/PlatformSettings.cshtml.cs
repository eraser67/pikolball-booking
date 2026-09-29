using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Organizations;

/// <summary>
/// Platform-owner page: toggle tenant self-registration on/off
/// and configure an optional registration welcome message.
/// </summary>
[Authorize(Policy = PlatformRoles.PlatformAdminPolicy)]
public class PlatformSettingsModel : PageModel
{
    private readonly PlatformSettingsService _platformSettings;

    public PlatformSettingsModel(PlatformSettingsService platformSettings)
    {
        _platformSettings = platformSettings;
    }

    [TempData]
    public string? StatusMessage { get; set; }

    [BindProperty]
    public SettingsInput Input { get; set; } = new();

    public async Task OnGetAsync()
    {
        var settings = await _platformSettings.GetAsync();
        Input.AllowTenantRegistration = settings.AllowTenantRegistration;
        Input.RegistrationMessage     = settings.RegistrationMessage;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        await _platformSettings.UpdateAsync(
            Input.AllowTenantRegistration,
            Input.RegistrationMessage);

        StatusMessage = "Platform settings saved.";
        return RedirectToPage();
    }

    public class SettingsInput
    {
        [Display(Name = "Allow public Court Owner registration")]
        public bool AllowTenantRegistration { get; set; }

        [MaxLength(600)]
        [Display(Name = "Registration page message")]
        public string? RegistrationMessage { get; set; }
    }
}
