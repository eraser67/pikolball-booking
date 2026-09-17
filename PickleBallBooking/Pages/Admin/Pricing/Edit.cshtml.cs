using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Pricing;

public class EditModel : PageModel
{
    private readonly IPricingService _pricingService;

    public EditModel(IPricingService pricingService)
    {
        _pricingService = pricingService;
    }

    [BindProperty]
    public int Id { get; set; }

    [BindProperty]
    public PricingInput Pricing { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var pricing = await _pricingService.GetByIdAsync(id);
        if (pricing is null)
        {
            return NotFound();
        }

        Id = pricing.Id;
        Pricing = new PricingInput
        {
            DayType = pricing.DayType,
            StartTime = pricing.StartTime,
            EndTime = pricing.EndTime,
            Price = pricing.Price
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Overnight bands are allowed (e.g. 18:00-02:00, or 18:00-00:00 for a band
        // that runs to midnight). Only a zero-length band is invalid.
        if (Pricing.StartTime == Pricing.EndTime)
        {
            ModelState.AddModelError(string.Empty, "End time must be different from start time.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var updated = await _pricingService.UpdateAsync(Id, Pricing.DayType, Pricing.StartTime, Pricing.EndTime, Pricing.Price);
        if (!updated)
        {
            return NotFound();
        }

        return RedirectToPage("Index");
    }

    public class PricingInput
    {
        [Required]
        [Display(Name = "Day Type")]
        public DayType DayType { get; set; }

        [Required]
        [Display(Name = "Start Time")]
        [DataType(DataType.Time)]
        public TimeSpan StartTime { get; set; }

        [Required]
        [Display(Name = "End Time")]
        [DataType(DataType.Time)]
        public TimeSpan EndTime { get; set; }

        [Required]
        [Range(0.01, 100000)]
        [Display(Name = "Price")]
        public decimal Price { get; set; }
    }
}

