using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Subscriptions.Plans;

[Authorize(Policy = PlatformRoles.PlatformAdminPolicy)]
public class IndexModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public IndexModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public List<SubscriptionPlan> Plans { get; set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        Plans = await _subscriptionService.GetAllPlansAsync();
    }

    public async Task<IActionResult> OnPostActivateAsync(int id)
    {
        await _subscriptionService.UpdatePlanAsync(
            id, string.Empty, null, 0, BillingPeriod.Monthly, null, null, null, isActive: true, isFree: false);
        StatusMessage = "Plan activated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeactivateAsync(int id)
    {
        var plan = await _subscriptionService.GetPlanByIdAsync(id);
        if (plan is not null)
        {
            await _subscriptionService.UpdatePlanAsync(
                id, plan.Name, plan.Description, plan.Price, plan.BillingPeriod,
                plan.MaxCourts, plan.MaxBookingsPerMonth, plan.Features,
                isActive: false, plan.IsFree);
        }
        StatusMessage = "Plan deactivated.";
        return RedirectToPage();
    }
}
