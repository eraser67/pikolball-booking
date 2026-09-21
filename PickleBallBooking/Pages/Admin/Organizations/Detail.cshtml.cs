using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Organizations;

[Authorize(Policy = PlatformRoles.PlatformAdminPolicy)]
public class DetailModel : PageModel
{
    private readonly IOrganizationService _organizationService;
    private readonly ISubscriptionService _subscriptionService;
    private readonly ICourtImageStorage _imageStorage;

    public DetailModel(
        IOrganizationService organizationService,
        ISubscriptionService subscriptionService,
        ICourtImageStorage imageStorage)
    {
        _organizationService = organizationService;
        _subscriptionService = subscriptionService;
        _imageStorage        = imageStorage;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public OrganizationSummary? Org { get; set; }
    public List<OrganizationMemberView> Members { get; set; } = [];
    public Subscription? CurrentSubscription { get; set; }

    /// <summary>Current hero image public URL for this organization (for display in platform admin view).</summary>
    public string? CurrentHeroImageUrl { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    [BindProperty]
    public AddMemberInput AddInput { get; set; } = new();

    [BindProperty]
    public HeroImageInput HeroInput { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        Org = await _organizationService.GetByIdAsync(Id);
        if (Org is null) return NotFound();

        Members             = await _organizationService.GetMembersOfOrgAsync(Id);
        CurrentSubscription = await _subscriptionService.GetForOrganizationAsync(Id);

        // Load the current hero image for platform admin preview.
        CurrentHeroImageUrl = _imageStorage.GetPublicUrl(Org.HeroImagePath);

        return Page();
    }

    // ── Activate / Deactivate ─────────────────────────────────────────────────

    public async Task<IActionResult> OnPostActivateAsync()
    {
        await _organizationService.SetStatusAsync(Id, OrganizationStatus.Active);
        StatusMessage = "Organization activated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeactivateAsync()
    {
        await _organizationService.SetStatusAsync(Id, OrganizationStatus.Inactive);
        StatusMessage = "Organization deactivated.";
        return RedirectToPage();
    }

    // ── Hero Image management (platform admin) ───────────────────────────────

    public async Task<IActionResult> OnPostUploadHeroImageAsync()
    {
        if (HeroInput.HeroImage is null || HeroInput.HeroImage.Length == 0)
        {
            ErrorMessage = "Please select an image file to upload.";
            return RedirectToPage();
        }

        try
        {
            var heroPath = await _imageStorage.UploadHeroImageAsync(Id, HeroInput.HeroImage);
            await _organizationService.UpdateHeroImageForOrgAsync(Id, heroPath);
            StatusMessage = "Hero image updated successfully.";
        }
        catch (CourtImageValidationException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Hero image upload failed: {ex.Message}";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveHeroImageAsync()
    {
        await _organizationService.UpdateHeroImageForOrgAsync(Id, null);
        StatusMessage = "Hero image removed. Default hero image will be shown.";
        return RedirectToPage();
    }

    // ── Member management ─────────────────────────────────────────────────────

    public async Task<IActionResult> OnPostAddMemberAsync()
    {
        Org = await _organizationService.GetByIdAsync(Id);
        if (Org is null) return NotFound();

        if (!ModelState.IsValid)
        {
            Members             = await _organizationService.GetMembersOfOrgAsync(Id);
            CurrentSubscription = await _subscriptionService.GetForOrganizationAsync(Id);
            return Page();
        }

        var (ok, error) = await _organizationService.AddMemberToOrgAsync(
            Id, AddInput.Email, AddInput.Role);

        if (ok)
            StatusMessage = $"{AddInput.Email} added as {AddInput.Role}.";
        else
            ErrorMessage = error ?? "Failed to add member.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveMemberAsync(string userId)
    {
        await _organizationService.RemoveMemberFromOrgAsync(Id, userId);
        StatusMessage = "Member removed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostChangeRoleAsync(string userId, OrganizationRole newRole)
    {
        await _organizationService.UpdateMemberRoleInOrgAsync(Id, userId, newRole);
        StatusMessage = "Role updated.";
        return RedirectToPage();
    }

    // ── Input models ──────────────────────────────────────────────────────────

    public class HeroImageInput
    {
        [Display(Name = "Hero Image (JPEG/PNG/WEBP, max 3 MB)")]
        public IFormFile? HeroImage { get; set; }
    }

    public class AddMemberInput
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [Display(Name = "Email Address")]
        public string Email { get; set; } = string.Empty;

        [Display(Name = "Role")]
        public OrganizationRole Role { get; set; } = OrganizationRole.OrganizationStaff;
    }
}
