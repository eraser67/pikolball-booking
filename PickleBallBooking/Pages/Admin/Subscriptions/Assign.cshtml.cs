using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using Microsoft.EntityFrameworkCore;

namespace PickleBallBooking.Pages.Admin.Subscriptions;

[Authorize(Policy = PlatformRoles.PlatformAdminPolicy)]
public class AssignModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ApplicationDbContext _context;

    public AssignModel(ISubscriptionService subscriptionService, ApplicationDbContext context)
    {
        _subscriptionService = subscriptionService;
        _context = context;
    }

    [BindProperty(SupportsGet = true)]
    public int OrganizationId { get; set; }

    public string OrganizationName { get; set; } = string.Empty;
    public string OrganizationSlug { get; set; } = string.Empty;
    public List<SubscriptionPlan> Plans { get; set; } = [];
    public Subscription? CurrentSubscription { get; set; }

    [BindProperty]
    public AssignInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var org = await _context.Organizations.FindAsync(OrganizationId);
        if (org is null) return NotFound();

        OrganizationName = org.Name;
        OrganizationSlug = org.Slug;
        Plans = await _subscriptionService.GetActivePlansAsync();
        CurrentSubscription = await _subscriptionService.GetForOrganizationAsync(OrganizationId);

        if (CurrentSubscription is not null)
        {
            Input = new AssignInput
            {
                PlanId       = CurrentSubscription.PlanId,
                Status       = CurrentSubscription.Status,
                StartDate    = CurrentSubscription.StartDate,
                EndDate      = CurrentSubscription.EndDate,
                TrialEndDate = CurrentSubscription.TrialEndDate,
                Notes        = CurrentSubscription.Notes
            };
        }
        else
        {
            // Default: Trial on first plan available
            Input.Status = SubscriptionStatus.Trial;
            Input.PlanId = Plans.FirstOrDefault()?.Id ?? 0;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Reload display-only data for any return Page() calls
        var org = await _context.Organizations.FindAsync(OrganizationId);
        if (org is null) return NotFound();
        OrganizationName = org.Name;
        OrganizationSlug = org.Slug;
        Plans = await _subscriptionService.GetActivePlansAsync();
        CurrentSubscription = await _subscriptionService.GetForOrganizationAsync(OrganizationId);

        if (!ModelState.IsValid) return Page();

        await _subscriptionService.AssignPlanAsync(
            OrganizationId,
            Input.PlanId,
            Input.Status,
            Input.StartDate,
            Input.EndDate,
            Input.TrialEndDate,
            Input.Notes);

        TempData["StatusMessage"] = $"Subscription for '{OrganizationName}' updated successfully.";
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostSetStatusAsync(string status)
    {
        var sub = await _subscriptionService.GetForOrganizationAsync(OrganizationId);
        if (sub is null) return NotFound();

        if (Enum.TryParse<SubscriptionStatus>(status, out var parsedStatus))
        {
            await _subscriptionService.UpdateStatusAsync(sub.Id, parsedStatus);
        }

        TempData["StatusMessage"] = $"Status updated to {status}.";
        return RedirectToPage("Index");
    }

    public class AssignInput
    {
        [Required(ErrorMessage = "Please select a plan.")]
        [Display(Name = "Plan")]
        public int PlanId { get; set; }

        [Display(Name = "Status")]
        public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Trial;

        [Display(Name = "Start Date")]
        public DateOnly? StartDate { get; set; }

        [Display(Name = "End Date")]
        public DateOnly? EndDate { get; set; }

        [Display(Name = "Trial End Date")]
        public DateOnly? TrialEndDate { get; set; }

        [MaxLength(1000)]
        [Display(Name = "Notes")]
        public string? Notes { get; set; }
    }
}
