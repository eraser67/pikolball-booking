using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
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

    public DetailModel(IOrganizationService organizationService, ISubscriptionService subscriptionService)
    {
        _organizationService = organizationService;
        _subscriptionService = subscriptionService;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public OrganizationSummary? Org { get; set; }
    public List<OrganizationMemberView> Members { get; set; } = [];
    public Subscription? CurrentSubscription { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    [BindProperty]
    public AddMemberInput AddInput { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        Org = await _organizationService.GetByIdAsync(Id);
        if (Org is null) return NotFound();

        Members             = await _organizationService.GetMembersOfOrgAsync(Id);
        CurrentSubscription = await _subscriptionService.GetForOrganizationAsync(Id);
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
