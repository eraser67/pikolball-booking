using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Subscriptions.Plans;

[Authorize(Policy = PlatformRoles.PlatformAdminPolicy)]
public class EditModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public EditModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty]
    public PlanInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var plan = await _subscriptionService.GetPlanByIdAsync(Id);
        if (plan is null) return NotFound();

        Input = new PlanInput
        {
            Name                = plan.Name,
            Description         = plan.Description,
            Price               = plan.Price,
            BillingPeriod       = plan.BillingPeriod,
            MaxCourts           = plan.MaxCourts,
            MaxBookingsPerMonth = plan.MaxBookingsPerMonth,
            Features            = plan.Features,
            IsActive            = plan.IsActive,
            IsFree              = plan.IsFree
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var ok = await _subscriptionService.UpdatePlanAsync(
            Id,
            Input.Name,
            Input.Description,
            Input.Price,
            Input.BillingPeriod,
            Input.MaxCourts,
            Input.MaxBookingsPerMonth,
            Input.Features,
            Input.IsActive,
            Input.IsFree);

        if (!ok)
        {
            ModelState.AddModelError("Input.Name", "A plan with this name already exists, or the plan was not found.");
            return Page();
        }

        TempData["StatusMessage"] = $"Plan '{Input.Name}' updated successfully.";
        return RedirectToPage("Index");
    }

    public class PlanInput
    {
        [Required(ErrorMessage = "Plan name is required.")]
        [MaxLength(100)]
        [Display(Name = "Plan Name")]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        [Display(Name = "Description")]
        public string? Description { get; set; }

        [Range(0, 9999999)]
        [Display(Name = "Price (₱)")]
        public decimal Price { get; set; }

        [Display(Name = "Billing Period")]
        public BillingPeriod BillingPeriod { get; set; } = BillingPeriod.Monthly;

        [Range(1, 10000)]
        [Display(Name = "Max Courts")]
        public int? MaxCourts { get; set; }

        [Range(1, 100000)]
        [Display(Name = "Max Bookings / Month")]
        public int? MaxBookingsPerMonth { get; set; }

        [MaxLength(2000)]
        [Display(Name = "Features (one per line)")]
        public string? Features { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Free Plan")]
        public bool IsFree { get; set; }
    }
}
