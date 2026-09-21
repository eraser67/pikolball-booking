using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Organizations;

[Authorize(Policy = PlatformRoles.PlatformAdminPolicy)]
public class IndexModel : PageModel
{
    private readonly IOrganizationService _organizationService;
    private readonly ISubscriptionService _subscriptionService;

    public IndexModel(IOrganizationService organizationService, ISubscriptionService subscriptionService)
    {
        _organizationService = organizationService;
        _subscriptionService = subscriptionService;
    }

    public List<OrganizationSummary> Organizations { get; set; } = [];

    /// <summary>Subscription lookup keyed by organizationId.</summary>
    public Dictionary<int, SubscriptionSummary> Subscriptions { get; set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        Organizations = await _organizationService.GetAllAsync();
        var allSubs   = await _subscriptionService.GetAllSubscriptionsAsync();
        Subscriptions = allSubs.ToDictionary(s => s.OrganizationId);
    }

    public async Task<IActionResult> OnPostActivateAsync(int id)
        => await SetStatusAsync(id, OrganizationStatus.Active, "activated");

    public async Task<IActionResult> OnPostDeactivateAsync(int id)
        => await SetStatusAsync(id, OrganizationStatus.Inactive, "deactivated");

    private async Task<IActionResult> SetStatusAsync(int id, OrganizationStatus status, string verb)
    {
        if (await _organizationService.SetStatusAsync(id, status))
            StatusMessage = $"Organization {verb} successfully.";
        else
            ErrorMessage = "Organization not found.";

        return RedirectToPage();
    }
}
