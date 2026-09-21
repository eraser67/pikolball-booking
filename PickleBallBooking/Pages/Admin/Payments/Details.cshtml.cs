using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Payments;

[Authorize(Policy = TenantAdminAuthorization.TenantAdminPolicy)]
public class DetailsModel : PageModel
{
    private readonly IPaymentService _paymentService;
    private readonly IPaymentProofStorage _proofStorage;

    public DetailsModel(IPaymentService paymentService, IPaymentProofStorage proofStorage)
    {
        _paymentService = paymentService;
        _proofStorage   = proofStorage;
    }

    public Payment? Payment { get; set; }

    /// <summary>Time-limited signed URL for the proof image (admin only).</summary>
    public string? ProofSignedUrl { get; set; }

    /// <summary>Raw storage path — used to detect when signing fails vs no proof uploaded.</summary>
    public string? ProofImagePath { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [BindProperty]
    public RejectInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Payment = await _paymentService.GetByIdAsync(id);
        if (Payment is null) return NotFound();

        ProofImagePath = Payment.ProofImagePath;

        if (!string.IsNullOrEmpty(Payment.ProofImagePath))
        {
            ProofSignedUrl = await _proofStorage.GetProofSignedUrlAsync(Payment.ProofImagePath);
        }

        return Page();
    }

    /// <summary>Admin verifies the payment.</summary>
    public async Task<IActionResult> OnPostVerifyAsync(int id)
    {
        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var ok = await _paymentService.VerifyAsync(id, adminId);

        StatusMessage = ok
            ? "Payment verified successfully."
            : "Unable to verify payment. It may have already been acted on.";

        return RedirectToPage(new { id });
    }

    /// <summary>Admin rejects the payment.</summary>
    public async Task<IActionResult> OnPostRejectAsync(int id)
    {
        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var ok = await _paymentService.RejectAsync(id, adminId, Input.Notes);

        StatusMessage = ok
            ? "Payment rejected."
            : "Unable to reject payment. It may have already been acted on.";

        return RedirectToPage(new { id });
    }

    public class RejectInput
    {
        [MaxLength(500)]
        [Display(Name = "Rejection Notes")]
        public string? Notes { get; set; }
    }
}
