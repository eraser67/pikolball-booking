using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Pricing;

public class IndexModel : PageModel
{
    private readonly IPricingService _pricingService;

    public IndexModel(IPricingService pricingService)
    {
        _pricingService = pricingService;
    }

    public List<PickleBallBooking.Models.Pricing> Pricings { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        Pricings = await _pricingService.GetAllAsync();
    }

    public async Task<IActionResult> OnPostToggleStatusAsync(int id)
    {
        var pricing = await _pricingService.GetByIdAsync(id);
        if (pricing is null)
        {
            return NotFound();
        }

        var newStatus = pricing.Status == PricingStatus.Active ? PricingStatus.Inactive : PricingStatus.Active;
        await _pricingService.SetStatusAsync(id, newStatus);

                StatusMessage = $"Pricing rule has been {(newStatus == PricingStatus.Active ? "activated" : "deactivated")}.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var pricing = await _pricingService.GetByIdAsync(id);
        if (pricing is null)
        {
            return NotFound();
        }

        await _pricingService.DeleteAsync(id);
        StatusMessage = "Pricing rule has been deleted.";

        return RedirectToPage();
    }
}
