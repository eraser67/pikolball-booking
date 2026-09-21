using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Booking;

public class PaymentConfirmationModel : PageModel
{
    private readonly IPaymentService _paymentService;

    public PaymentConfirmationModel(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public Services.PaymentLookup? PaymentInfo { get; set; }
    public bool AlreadySubmitted { get; set; }

    public async Task OnGetAsync(string reference, bool alreadySubmitted = false)
    {
        AlreadySubmitted = alreadySubmitted;
        PaymentInfo      = await _paymentService.GetByBookingReferenceAsync(reference);
    }
}
