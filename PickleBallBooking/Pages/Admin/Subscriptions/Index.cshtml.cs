using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Subscriptions;

[Authorize(Policy = PlatformRoles.PlatformAdminPolicy)]
public class IndexModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ApplicationDbContext _context;

    public IndexModel(ISubscriptionService subscriptionService, ApplicationDbContext context)
    {
        _subscriptionService = subscriptionService;
        _context = context;
    }

    public List<SubscriptionSummary> Subscriptions { get; set; } = [];

    /// <summary>Organizations that have no subscription row yet.</summary>
    public List<Organization> UnsubscribedOrgs { get; set; } = [];

    [Microsoft.AspNetCore.Mvc.TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        Subscriptions = await _subscriptionService.GetAllSubscriptionsAsync();

        var subscribedOrgIds = Subscriptions.Select(s => s.OrganizationId).ToHashSet();

        UnsubscribedOrgs = await _context.Organizations
            .Where(o => !subscribedOrgIds.Contains(o.Id))
            .OrderBy(o => o.Name)
            .ToListAsync();
    }
}
