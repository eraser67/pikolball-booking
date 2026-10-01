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
/// Two handlers:
///   OnPostAsync        — save profile fields (name, skill, bio, etc.)
///   OnPostAvatarAsync  — upload a new profile photo (separate form, Ajax-friendly)
/// </summary>
[RequestFormLimits(MultipartBodyLengthLimit = 3_000_000)]
public class ProfileModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly PlayerProfileService _profileService;
    private readonly ICourtImageStorage _storage;

    public ProfileModel(
        UserManager<IdentityUser> userManager,
        PlayerProfileService profileService,
        ICourtImageStorage storage)
    {
        _userManager    = userManager;
        _profileService = profileService;
        _storage        = storage;
    }

    [BindProperty]
    public ProfileInput Input { get; set; } = new();

    public string? StatusMessage { get; set; }
    public bool IsNewProfile { get; set; }
    public string? AvatarUrl { get; set; }

    public async Task<IActionResult> OnGetAsync(bool? saved = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return RedirectToPage("/Account/Login");

        var profile = await _profileService.GetByUserIdAsync(user.Id);
        IsNewProfile = profile is null;
        AvatarUrl    = _storage.GetPublicUrl(profile?.AvatarPath);

        if (saved == true)
        {
            StatusMessage = "Profile saved successfully.";
        }

        if (profile is not null)
        {
            Input.FirstName      = profile.FirstName;
            Input.LastName       = profile.LastName;
            Input.DisplayName    = profile.DisplayName;
            Input.Mobile         = profile.Mobile ?? string.Empty;
            Input.SkillLevel     = profile.SkillLevel;
            Input.PlayingHand    = profile.PlayingHand;
            Input.Bio            = profile.Bio ?? string.Empty;
            Input.Location       = profile.Location ?? string.Empty;
            Input.IsDiscoverable       = profile.IsDiscoverable;
            Input.PrivacyMobile        = profile.PrivacyMobile;
            Input.PrivacyMatchHistory  = profile.PrivacyMatchHistory;
        }
        else
        {
            // Pre-fill from Phase 31 registration claims.
            var claims   = await _userManager.GetClaimsAsync(user);
            var fullName = claims.FirstOrDefault(c => c.Type == "fullName")?.Value ?? string.Empty;
            var parts    = fullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            Input.FirstName = parts.Length > 0 ? parts[0] : string.Empty;
            Input.LastName  = parts.Length > 1 ? parts[1] : string.Empty;
            Input.Mobile    = claims.FirstOrDefault(c => c.Type == "mobile")?.Value
                              ?? user.PhoneNumber ?? string.Empty;
            Input.PrivacyMatchHistory = MatchHistoryPrivacyLevel.Public;
        }

        return Page();
    }

    /// <summary>Save profile fields (name, bio, skill, etc.).</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            var user2 = await _userManager.GetUserAsync(User);
            var p2 = user2 is not null ? await _profileService.GetByUserIdAsync(user2.Id) : null;
            AvatarUrl = _storage.GetPublicUrl(p2?.AvatarPath);
            return Page();
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return RedirectToPage("/Account/Login");

        await _profileService.UpsertAsync(
            userId:               user.Id,
            firstName:            Input.FirstName,
            lastName:             Input.LastName,
            displayName:          Input.DisplayName,
            mobile:               Input.Mobile,
            skillLevel:           Input.SkillLevel,
            playingHand:          Input.PlayingHand,
            bio:                  Input.Bio,
            location:             Input.Location,
            isDiscoverable:       Input.IsDiscoverable,
            privacyMobile:        Input.PrivacyMobile,
            privacyMatchHistory:  Input.PrivacyMatchHistory);

        // Sync phone number back to Identity user.
        if (!string.IsNullOrEmpty(Input.Mobile) && user.PhoneNumber != Input.Mobile)
        {
            await _userManager.SetPhoneNumberAsync(user, Input.Mobile);
        }

        return RedirectToPage(new { saved = true });
    }

    /// <summary>Upload a new profile photo. Separate handler so it works independently.</summary>
    public async Task<IActionResult> OnPostAvatarAsync(IFormFile avatarFile)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return RedirectToPage("/Account/Login");

        if (avatarFile is null || avatarFile.Length == 0)
        {
            StatusMessage = "Please choose an image file to upload.";
            return await OnGetAsync();
        }

        try
        {
            await _profileService.UploadAvatarAsync(user.Id, avatarFile);
            StatusMessage = "Profile photo updated successfully.";
        }
        catch (CourtImageValidationException ex)
        {
            StatusMessage = $"Upload failed: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            // Profile doesn't exist yet — prompt to save profile first.
            StatusMessage = ex.Message;
        }

        return await OnGetAsync();
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

        [Display(Name = "Match History & Statistics Visibility")]
        public MatchHistoryPrivacyLevel PrivacyMatchHistory { get; set; } = MatchHistoryPrivacyLevel.Public;
    }
}
