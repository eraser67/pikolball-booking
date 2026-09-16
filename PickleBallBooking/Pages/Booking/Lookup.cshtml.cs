using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Booking;

public class LookupModel : PageModel
{
    private readonly IBookingService _bookingService;

    public LookupModel(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    [BindProperty]
    public LookupInput Input { get; set; } = new();

    public Models.Booking? Booking { get; set; }

    public bool Searched { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        Searched = true;
        Booking = await _bookingService.LookupBookingAsync(Input.BookingReference, Input.ContactInfo);

        return Page();
    }

    public class LookupInput
    {
        [Required(ErrorMessage = "Please enter your booking reference.")]
        [Display(Name = "Booking Reference")]
        public string BookingReference { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your mobile number or email.")]
        [Display(Name = "Mobile Number or Email")]
        public string ContactInfo { get; set; } = string.Empty;
    }
}
