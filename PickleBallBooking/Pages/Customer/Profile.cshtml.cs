using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Customer;

/// <summary>
/// Phase 32: player profile edit page.
///
/// Loads the existing profile (if any) and allows the authenticated customer
/// to create or update their player profile. All fields are platform-global
/// (no OrganizationId). Profile data migrates from Phase 31 claims on first save.
/// </summary>
public class ProfileModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly PlayerProfileService _profileService;

    public ProfileModel(
        UserManager<IdentityUser> userManager,
        PlayerProfileService profileService)
    {
        _userManager    = userManager;
        _profileService = profileService;
    }

    [BindProperty]
    public ProfileInput Input { get; set; } = new();

    public string? StatusMessage { get; set; }
    public bool IsNewProfile { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return RedirectToPage("/Account/Login");

        var profile = await _profileService.GetByUserIdAsync(user.Id);
        IsNewProfile = profile is null;

        if (profile is not null)
        {
            // Populate form from saved profile.
            Input.FirstName      = profile.FirstName;
            Input.LastName       = profile.LastName;
            Input.DisplayName    = profile.DisplayName;
            Input.Mobile         = profile.Mobile ?? string.Empty;
            Input.SkillLevel     = profile.SkillLevel;
            Input.PlayingHand    = profile.PlayingHand;
            Input.Bio            = profile.Bio ?? string.Empty;
            Input.Location       = profile.Location ?? string.Empty;
            Input.IsDiscoverable = profile.IsDiscoverable;
            Input.PrivacyMobile  = profile.PrivacyMobile;
        }
        else
        {
            // Pre-fill from Phase 31 claims (name + mobile stored at registration).
            var claims = await _userManager.GetClaimsAsync(user);
            var fullName = claims.FirstOrDefault(c => c.Type == "fullName")?.Value ?? string.Empty;
            var nameParts = fullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            Input.FirstName = nameParts.Length > 0 ? nameParts[0] : string.Empty;
            Input.LastName  = nameParts.Length > 1 ? nameParts[1] : string.Empty;
            Input.Mobile    = claims.FirstOrDefault(c => c.Type == "mobile")?.Value ?? user.PhoneNumber ?? string.Empty;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return RedirectToPage("/Account/Login");

        await _profileService.UpsertAsync(
            userId:        user.Id,
            firstName:     Input.FirstName,
            lastName:      Input.LastName,
            displayName:   Input.DisplayName,
            mobile:        Input.Mobile,
            skillLevel:    Input.SkillLevel,
            playingHand:   Input.PlayingHand,
            bio:           Input.Bio,
            location:      Input.Location,
            isDiscoverable: Input.IsDiscoverable,
            privacyMobile:  Input.PrivacyMobile);

        // Also sync the phone number back to the Identity user record.
        if (user.PhoneNumber != Input.Mobile)
        {
            await _userManager.SetPhoneNumberAsync(user, Input.Mobile);
        }

        StatusMessage = "Profile saved successfully.";
        return RedirectToPage(new { saved = true });
    }

    public class ProfileInput
    {
        [Required(ErrorMessage = "First name is required.")]
        [MaxLength(50)]
        [Display(Name = "First Name")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Last name is required.")]
        [MaxLength(50)]
        [Display(Name = "Last Name")]
        public string LastName { get; set; } = string.Empty;

        [MaxLength(100)]
        [Display(Name = "Display Name")]
        public string? DisplayName { get; set; }

        [Phone]
        [MaxLength(20)]
        [Display(Name = "Mobile Number")]
        public string? Mobile { get; set; }

        [Display(Name = "Skill Level")]
        public PlayerSkillLevel SkillLevel { get; set; } = PlayerSkillLevel.Beginner;

        [Display(Name = "Playing Hand")]
        public PlayingHand PlayingHand { get; set; } = PlayingHand.Right;

        [MaxLength(500)]
        [Display(Name = "About Me")]
        public string? Bio { get; set; }

        [MaxLength(100)]
        [Display(Name = "Location (City / Region)")]
        public string? Location { get; set; }

        [Display(Name = "Make my profile discoverable")]
        public bool IsDiscoverable { get; set; } = true;

        [Display(Name = "Show mobile number to other players")]
        public bool PrivacyMobile { get; set; } = false;
    }
}
