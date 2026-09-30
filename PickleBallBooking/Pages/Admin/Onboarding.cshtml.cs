using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin;

/// <summary>
/// First-time setup wizard shown to newly activated organization owners.
/// Guides them through: branding → courts → time slots → pricing.
/// </summary>
[Authorize]
public class OnboardingModel : PageModel
{
    private readonly IOrganizationService _orgService;

    public OnboardingModel(IOrganizationService orgService)
    {
        _orgService = orgService;
    }

    public string OrgName { get; set; } = "Your Venue";

    public async Task OnGetAsync()
    {
        var org = await _orgService.GetCurrentAsync();
        if (org is not null) OrgName = org.Name;
    }
}
