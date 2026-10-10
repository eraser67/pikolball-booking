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

    /// <summary>The specific TenantPaymentOption chosen by the customer (e.g. Maya, GCash, Bank QR).</summary>
    public Models.TenantPaymentOption? SelectedPaymentOption { get; set; }

    /// <summary>The payment record created/loaded for this booking.</summary>
    public Models.Payment? Payment { get; set; }

    /// <summary>Public URL of the QR code image if configured.</summary>
    public string? QRCodeUrl { get; set; }

    /// <summary>True if the organization has enabled AI auto-verification in Org Settings.</summary>
    public bool EnableAiPaymentVerification { get; set; }

    public async Task OnGetAsync(string reference)
    {
        Booking = await _context.Bookings
            .IgnoreQueryFilters()
            .Include(b => b.Court)
            .Include(b => b.TimeSlots)
                .ThenInclude(bts => bts.TimeSlot)
            .FirstOrDefaultAsync(b => b.BookingReference == reference);

        if (Booking is null) return;

        // Load organization settings & AI verification toggle
        var org = await _context.Organizations
            .IgnoreQueryFilters()
            .Where(o => o.Id == Booking.OrganizationId)
            .Select(o => new { o.EnableAiPaymentVerification })
            .FirstOrDefaultAsync();
        EnableAiPaymentVerification = org?.EnableAiPaymentVerification == true;

        // Load organization's legacy payment settings for the booking's venue organization.
        PaymentSettings = await _paymentService.GetPaymentSettingsForOrgAsync(Booking.OrganizationId);

        // Load the specific TenantPaymentOption chosen by the customer
        if (Booking.SelectedPaymentOptionId is int optionId and > 0)
        {
            SelectedPaymentOption = await _paymentService.GetPaymentOptionByIdNoFilterAsync(optionId);
        }

        // Determine QR code public URL: prefer chosen payment option, fallback to org payment settings
        if (!string.IsNullOrWhiteSpace(SelectedPaymentOption?.QRCodeImagePath))
        {
            QRCodeUrl = _proofStorage.GetQRCodePublicUrl(SelectedPaymentOption.QRCodeImagePath);
        }
        else if (!string.IsNullOrWhiteSpace(PaymentSettings?.QRCodeImagePath))
        {
            QRCodeUrl = _proofStorage.GetQRCodePublicUrl(PaymentSettings.QRCodeImagePath);
        }

        // Load payment record for this booking so the pass and status are always accurate.
        Payment = await _context.Payments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.BookingId == Booking.Id);

        if (Payment is null && (SelectedPaymentOption is not null || PaymentSettings?.IsActive == true))
        {
            Payment = await _paymentService.CreateForBookingAsync(Booking.Id);
        }
    }
}
