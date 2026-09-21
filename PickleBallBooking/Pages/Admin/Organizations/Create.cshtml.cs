using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Organizations;

/// <summary>
/// Phase 23: platform-only "Create Organization" page.
///
/// The platform admin supplies:
///  - Organization Name
///  - Subdomain (slug) - ONLY the label; the base domain comes from configuration
///  - Owner Name
///  - Owner Email
///
/// The resulting hostname is built from the configured base domain; the admin cannot
/// enter an arbitrary full domain. Creation is atomic (organization + owner membership)
/// and reuses an existing Identity user when the email already has an account.
/// </summary>
[Authorize(Policy = PlatformRoles.PlatformAdminPolicy)]
public class CreateModel : PageModel
{
    private readonly IOrganizationService _organizationService;
    private readonly TenantOptions _tenantOptions;

    public CreateModel(IOrganizationService organizationService, IOptions<TenantOptions> tenantOptions)
    {
        _organizationService = organizationService;
        _tenantOptions = tenantOptions.Value;
    }

    [BindProperty]
    public OrganizationInput Input { get; set; } = new();

    public string BaseDomain => _tenantOptions.BaseDomain;

    /// <summary>Shown after a successful create when a brand-new owner was created.</summary>
    public string? ActivationLink { get; set; }

    public string? CreatedHostname { get; set; }

    public string? ReusedOwnerEmail { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _organizationService.CreateAsync(
            Input.Name,
            Input.Slug,
            Input.OwnerName,
            Input.OwnerEmail);

        if (!result.Success)
        {
            foreach (var (field, message) in result.Errors)
            {
                ModelState.AddModelError($"Input.{field}", message);
            }

            return Page();
        }

        CreatedHostname = $"{result.Organization!.Slug}.{_tenantOptions.BaseDomain}";

        if (result.OwnerAccountCreated && !string.IsNullOrEmpty(result.OwnerActivationToken))
        {
            // Build an absolute activation link from the Identity token. In this phase
            // (no email delivery) it is shown to the platform admin; the later email
            // phase will send the SAME link to the owner instead.
            ActivationLink = Url.Page(
                "/Account/Activate",
                pageHandler: null,
                values: new { userId = result.OwnerUserId, token = result.OwnerActivationToken },
                protocol: Request.Scheme);
        }
        else
        {
            ReusedOwnerEmail = Input.OwnerEmail.Trim();
        }

        return Page();
    }

    public class OrganizationInput
    {
        [Required(ErrorMessage = "Organization name is required.")]
        [StringLength(150)]
        [Display(Name = "Organization Name")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Subdomain is required.")]
        [StringLength(63)]
        [RegularExpression("^[a-z0-9]([a-z0-9-]*[a-z0-9])?$",
            ErrorMessage = "Use lowercase letters, numbers and hyphens only (no dots or spaces), not starting or ending with a hyphen.")]
        [Display(Name = "Subdomain")]
        public string Slug { get; set; } = string.Empty;

        [Required(ErrorMessage = "Owner name is required.")]
        [StringLength(150)]
        [Display(Name = "Owner Name")]
        public string OwnerName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Owner email is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [Display(Name = "Owner Email")]
        public string OwnerEmail { get; set; } = string.Empty;
    }
}
