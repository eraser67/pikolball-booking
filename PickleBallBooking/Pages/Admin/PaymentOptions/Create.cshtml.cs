using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.PaymentOptions;

[Authorize(Policy = TenantAdminAuthorization.TenantAdminPolicy)]
public class CreateModel : PageModel
{
    private readonly IPaymentService _paymentService;
    private readonly IPaymentProofStorage _proofStorage;
    private readonly IOrganizationService _organizationService;

    public CreateModel(
        IPaymentService paymentService,
        IPaymentProofStorage proofStorage,
        IOrganizationService organizationService)
    {
        _paymentService      = paymentService;
        _proofStorage        = proofStorage;
        _organizationService = organizationService;
    }

    [BindProperty]
    public PaymentOptionInput Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        // Pre-fill display order = current count + 1
        var existing = await _paymentService.GetAllPaymentOptionsAsync();
        Input.DisplayOrder = existing.Count;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        string? qrPath = null;

        if (Input.QRCodeImage is { Length: > 0 })
        {
            var org = await _organizationService.GetCurrentAsync();
            if (org is null) return NotFound();

            try
            {
                qrPath = await _proofStorage.UploadQRCodeAsync(org.Id, Input.QRCodeImage);
            }
            catch (CourtImageValidationException ex)
            {
                ModelState.AddModelError("Input.QRCodeImage", ex.Message);
                return Page();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("Input.QRCodeImage", $"QR upload failed: {ex.Message}");
                return Page();
            }
        }

        var result = await _paymentService.CreatePaymentOptionAsync(
            Input.Label,
            Input.AccountName,
            Input.AccountNumber,
            Input.Instructions,
            qrPath,
            Input.DisplayOrder);

        if (result is null)
        {
            ModelState.AddModelError(string.Empty, "Failed to create payment option. Please try again.");
            return Page();
        }

        StatusMessage = $"Payment method '{Input.Label}' added successfully.";
        return RedirectToPage("Index");
    }

    public class PaymentOptionInput
    {
        [Required(ErrorMessage = "Name is required (e.g. GCash, Maya, BDO Bank QR).")]
        [MaxLength(100)]
        [Display(Name = "Payment Method Name")]
        public string Label { get; set; } = string.Empty;

        [Required(ErrorMessage = "Account holder name is required.")]
        [MaxLength(100)]
        [Display(Name = "Account Name")]
        public string AccountName { get; set; } = string.Empty;

        [MaxLength(100)]
        [Display(Name = "Account Number / Mobile Number")]
        public string? AccountNumber { get; set; }

        [MaxLength(1000)]
        [Display(Name = "Instructions for customers (optional)")]
        public string? Instructions { get; set; }

        [Display(Name = "QR Code Image (optional, JPEG/PNG/WEBP, max 1MB)")]
        public IFormFile? QRCodeImage { get; set; }

        [Display(Name = "Display Order")]
        [Range(0, 999)]
        public int DisplayOrder { get; set; }
    }
}
