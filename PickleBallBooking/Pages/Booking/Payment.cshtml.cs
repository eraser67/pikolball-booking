using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using PickleBallBooking.Services;
using Microsoft.AspNetCore.RateLimiting;

namespace PickleBallBooking.Pages.Booking;

/// <summary>
/// Customer-facing GCash payment submission page.
/// No authentication required — the customer arrives via the booking reference link.
/// The customer can ONLY submit a reference number; they cannot set their own status to Verified.
/// </summary>
[EnableRateLimiting("payment-limit")]
public class PaymentModel : PageModel
{
    private readonly IPaymentService _paymentService;
    private readonly IPaymentProofStorage _proofStorage;

    public PaymentModel(IPaymentService paymentService, IPaymentProofStorage proofStorage)
    {
        _paymentService = paymentService;
        _proofStorage   = proofStorage;
    }

    public Services.PaymentLookup? PaymentInfo { get; set; }

    public Models.OrganizationPaymentSettings? Settings { get; set; }

    public string? QRCodeUrl { get; set; }

    [BindProperty]
    public PaymentInput Input { get; set; } = new();

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(string reference)
    {
        PaymentInfo = await _paymentService.GetByBookingReferenceAsync(reference);
        if (PaymentInfo is null) return NotFound();

        if (PaymentInfo.Status == Models.PaymentStatus.Submitted ||
            PaymentInfo.Status == Models.PaymentStatus.Verified)
        {
            // Already submitted — redirect to confirmation.
            return RedirectToPage("/Booking/PaymentConfirmation",
                new { reference, alreadySubmitted = true });
        }

        if (PaymentInfo.Status != Models.PaymentStatus.Pending)
        {
            return RedirectToPage("/Booking/PaymentConfirmation", new { reference });
        }

        await LoadSettingsAsync(reference);
        Input.Reference = reference;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        PaymentInfo = await _paymentService.GetByBookingReferenceAsync(Input.Reference ?? string.Empty);
        if (PaymentInfo is null) return NotFound();

        if (!ModelState.IsValid)
        {
            await LoadSettingsAsync(Input.Reference ?? string.Empty);
            return Page();
        }

        if (PaymentInfo.Status != Models.PaymentStatus.Pending)
        {
            return RedirectToPage("/Booking/PaymentConfirmation", new { reference = Input.Reference });
        }

        // Upload proof if provided.
        string? proofPath = null;
        if (Input.ProofImage is not null && Input.ProofImage.Length > 0)
        {
            try
            {
                // organizationId is from PaymentInfo (server-loaded) — NEVER from client input.
                // paymentId is the server-assigned payment PK — NEVER from client input.
                proofPath = await _proofStorage.UploadProofAsync(
                    organizationId: PaymentInfo.OrganizationId,
                    paymentId: PaymentInfo.PaymentId,
                    file: Input.ProofImage);
            }
            catch (CourtImageValidationException ex)
            {
                // Invalid or oversized proof: reject without touching the existing payment record.
                ModelState.AddModelError("Input.ProofImage", ex.Message);
                await LoadSettingsAsync(Input.Reference ?? string.Empty);
                return Page();
            }
            catch (InvalidOperationException)
            {
                // Supabase storage upload failed (e.g. network, bucket permissions).
                // Show the customer a clear message instead of silently submitting without proof.
                ModelState.AddModelError("Input.ProofImage",
                    "Could not upload your payment screenshot. Please try again, or submit without a screenshot.");
                await LoadSettingsAsync(Input.Reference ?? string.Empty);
                return Page();
            }
            catch
            {
                // Other unexpected errors — proceed without proof rather than blocking submission.
            }
        }

        var ok = await _paymentService.SubmitAsync(
            PaymentInfo.PaymentId,
            Input.GCashReference ?? string.Empty,
            proofPath);

        if (!ok)
        {
            ModelState.AddModelError(string.Empty, "Unable to submit payment. Please try again.");
            await LoadSettingsAsync(Input.Reference ?? string.Empty);
            return Page();
        }

        return RedirectToPage("/Booking/PaymentConfirmation", new { reference = Input.Reference });
    }

    private async Task LoadSettingsAsync(string reference)
    {
        if (PaymentInfo is not null)
        {
            Settings = await _paymentService.GetPaymentSettingsForOrgAsync(PaymentInfo.OrganizationId);
        }
        else
        {
            Settings = await _paymentService.GetPaymentSettingsAsync();
        }
        QRCodeUrl = _proofStorage.GetQRCodePublicUrl(Settings?.QRCodeImagePath);
    }

    public class PaymentInput
    {
        public string? Reference { get; set; }

        [Required(ErrorMessage = "Please enter your GCash reference number.")]
        [MaxLength(100)]
        [Display(Name = "GCash Reference Number")]
        public string? GCashReference { get; set; }

        [Display(Name = "Payment Screenshot (optional)")]
        public IFormFile? ProofImage { get; set; }
    }
}
