using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin;

/// <summary>
/// Phase 26: subscription wall page shown to org admins when their
/// organization's subscription is Expired, Suspended, or Cancelled.
/// Platform admins bypass this page entirely (SubscriptionWallMiddleware skips them).
/// </summary>
[Authorize(Policy = TenantAdminAuthorization.TenantAdminPolicy)]
public class SubscriptionRequiredModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public SubscriptionRequiredModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public Models.Subscription? CurrentSubscription { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        // If the subscription is now OK (admin reactivated), redirect back to dashboard.
        if (await _subscriptionService.CanAcceptBookingsAsync())
        {
            return RedirectToPage("/Admin/Index");
        }

        CurrentSubscription = await _subscriptionService.GetCurrentAsync();
        return Page();
    }
}
