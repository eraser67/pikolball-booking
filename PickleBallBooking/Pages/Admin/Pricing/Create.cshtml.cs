using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Pricing;

public class CreateModel : PageModel
{
    private readonly IPricingService _pricingService;

    public CreateModel(IPricingService pricingService)
    {
        _pricingService = pricingService;
    }

    [BindProperty]
    public PricingInput Pricing { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Pricing.EndTime <= Pricing.StartTime)
        {
            ModelState.AddModelError(string.Empty, "End time must be after start time.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        await _pricingService.CreateAsync(Pricing.DayType, Pricing.StartTime, Pricing.EndTime, Pricing.Price);

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
