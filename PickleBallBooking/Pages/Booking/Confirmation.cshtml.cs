using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Booking;

/// <summary>
/// Shows booking details + "Pay with GCash" CTA if the organization has
/// active payment settings. The payment is created at this point if it doesn't
/// already exist.
/// </summary>
public class ConfirmationModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly IPaymentService _paymentService;
    private readonly IPaymentProofStorage _proofStorage;

    public ConfirmationModel(
        ApplicationDbContext context,
        IPaymentService paymentService,
        IPaymentProofStorage proofStorage)
    {
        _context        = context;
        _paymentService = paymentService;
        _proofStorage   = proofStorage;
    }

    public Models.Booking? Booking { get; set; }

    /// <summary>GCash payment settings for the booking's tenant (null if not configured).</summary>
    public Models.OrganizationPaymentSettings? PaymentSettings { get; set; }

    /// <summary>The payment record created/loaded for this booking.</summary>
    public Models.Payment? Payment { get; set; }

    /// <summary>Public URL of the GCash QR code image if configured.</summary>
    public string? QRCodeUrl { get; set; }

    public async Task OnGetAsync(string reference)
    {
        Booking = await _context.Bookings
            .IgnoreQueryFilters()
            .Include(b => b.Court)
            .Include(b => b.TimeSlots)
                .ThenInclude(bts => bts.TimeSlot)
            .FirstOrDefaultAsync(b => b.BookingReference == reference);

        if (Booking is null) return;

        // Load organization's payment settings for the booking's venue organization.
        PaymentSettings = await _paymentService.GetPaymentSettingsForOrgAsync(Booking.OrganizationId);
        QRCodeUrl       = _proofStorage.GetQRCodePublicUrl(PaymentSettings?.QRCodeImagePath);

        // Auto-create a pending payment record for this booking so the customer
        // has something to submit a reference number against.
        if (PaymentSettings?.IsActive == true)
        {
            Payment = await _paymentService.CreateForBookingAsync(Booking.Id);
        }
    }
}
