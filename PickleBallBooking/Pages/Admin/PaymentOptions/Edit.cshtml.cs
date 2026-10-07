using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.PaymentOptions;

[Authorize(Policy = TenantAdminAuthorization.TenantAdminPolicy)]
public class EditModel : PageModel
{
    private readonly IPaymentService _paymentService;
    private readonly IPaymentProofStorage _proofStorage;
    private readonly IOrganizationService _organizationService;

    public EditModel(
        IPaymentService paymentService,
        IPaymentProofStorage proofStorage,
        IOrganizationService organizationService)
    {
        _paymentService      = paymentService;
        _proofStorage        = proofStorage;
        _organizationService = organizationService;
    }

    public string? CurrentQRCodeUrl { get; set; }
    public string? CurrentQRCodePath { get; set; }

    [BindProperty]
    public PaymentOptionInput Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var option = await _paymentService.GetPaymentOptionByIdAsync(id);
        if (option is null) return NotFound();

        CurrentQRCodePath   = option.QRCodeImagePath;
        CurrentQRCodeUrl    = _proofStorage.GetQRCodePublicUrl(option.QRCodeImagePath);

        Input.Id            = option.Id;
        Input.Label         = option.Label;
        Input.AccountName   = option.AccountName;
        Input.AccountNumber = option.AccountNumber;
        Input.Instructions  = option.Instructions;
        Input.IsActive      = option.IsActive;
        Input.DisplayOrder  = option.DisplayOrder;
        Input.RemoveQRCode  = false;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var existing = await _paymentService.GetPaymentOptionByIdAsync(Input.Id);
        if (existing is null) return NotFound();

        string? qrPath = null;  // null = keep existing

        if (Input.RemoveQRCode)
        {
            qrPath = string.Empty; // empty string = clear it
        }
        else if (Input.QRCodeImage is { Length: > 0 })
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
                CurrentQRCodeUrl  = _proofStorage.GetQRCodePublicUrl(existing.QRCodeImagePath);
                CurrentQRCodePath = existing.QRCodeImagePath;
                return Page();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("Input.QRCodeImage", $"Upload failed: {ex.Message}");
                CurrentQRCodeUrl  = _proofStorage.GetQRCodePublicUrl(existing.QRCodeImagePath);
                CurrentQRCodePath = existing.QRCodeImagePath;
                return Page();
            }
        }

        // Treat empty string as null for UpdatePaymentOptionAsync (it only overwrites on non-null)
        string? pathToSet = qrPath == string.Empty ? null
                          : qrPath;                    // null keeps existing, non-null updates

        // When RemoveQRCode, we need to explicitly set null in the DB
        if (Input.RemoveQRCode)
        {
            // Pass an empty string sentinel so UpdatePaymentOptionAsync overwrites with null
            // Actually let's handle it directly: clear via update
        }

        await _paymentService.UpdatePaymentOptionAsync(
            Input.Id,
            Input.Label,
            Input.AccountName,
            Input.AccountNumber,
            Input.Instructions,
            qrPath,     // null = keep, "" = clear, path = update
            Input.IsActive,
            Input.DisplayOrder);

        StatusMessage = $"'{Input.Label}' updated.";
        return RedirectToPage("Index");
    }

    public class PaymentOptionInput
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Name is required.")]
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

        [Display(Name = "Replace QR Code Image")]
        public IFormFile? QRCodeImage { get; set; }

        [Display(Name = "Remove existing QR code")]
        public bool RemoveQRCode { get; set; }

        [Display(Name = "Active (visible to customers)")]
        public bool IsActive { get; set; } = true;

        [Range(0, 999)]
        [Display(Name = "Display Order")]
        public int DisplayOrder { get; set; }
    }
}
