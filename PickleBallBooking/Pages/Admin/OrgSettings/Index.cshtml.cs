using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.OrgSettings;

[Authorize(Policy = TenantAdminAuthorization.TenantAdminPolicy)]
public class IndexModel : PageModel
{
    private readonly IOrganizationService _organizationService;
    private readonly IPaymentService _paymentService;
    private readonly IPaymentProofStorage _proofStorage;
    private readonly ICourtImageStorage _imageStorage;
    private readonly ISubscriptionService _subscriptionService;

    public IndexModel(
        IOrganizationService organizationService,
        IPaymentService paymentService,
        IPaymentProofStorage proofStorage,
        ICourtImageStorage imageStorage,
        ISubscriptionService subscriptionService)
    {
        _organizationService = organizationService;
        _paymentService      = paymentService;
        _proofStorage        = proofStorage;
        _imageStorage        = imageStorage;
        _subscriptionService = subscriptionService;
    }

    public string? Slug { get; set; }
    public OrganizationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Current subscription for this tenant (read-only for org admins).</summary>
    public Subscription? CurrentSubscription { get; set; }

    /// <summary>Current QR code public URL (for display).</summary>
    public string? CurrentQRCodeUrl { get; set; }

    /// <summary>Current custom brand logo public URL (for display).</summary>
    public string? CurrentLogoUrl { get; set; }

    [BindProperty]
    public SettingsInput Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var organization = await _organizationService.GetCurrentAsync();
        if (organization is null) return NotFound();

        Slug      = organization.Slug;
        Status    = organization.Status;
        CreatedAt = organization.CreatedAt;

        Input.Name      = organization.Name;
        Input.Address   = organization.Address;
        Input.Latitude  = organization.Latitude;
        Input.Longitude = organization.Longitude;
        Input.NotificationEmail = organization.NotificationEmail;

        CurrentLogoUrl = _imageStorage.GetPublicUrl(organization.LogoPath);

        // Load current payment settings.
        var paySettings = await _paymentService.GetPaymentSettingsAsync();
        if (paySettings is not null)
        {
            Input.AccountName   = paySettings.AccountName;
            Input.AccountNumber = paySettings.AccountNumber;
            Input.Instructions  = paySettings.Instructions;
            Input.GCashEnabled  = paySettings.IsActive;
            CurrentQRCodeUrl    = _proofStorage.GetQRCodePublicUrl(paySettings.QRCodeImagePath);
        }

        // Phase 26: load subscription for read-only display.
        CurrentSubscription = await _subscriptionService.GetCurrentAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            await ReloadReadOnlyAsync();
            return Page();
        }

        // Save name.
        var updated = await _organizationService.RenameCurrentAsync(Input.Name);
        if (!updated)
        {
            ModelState.AddModelError("Input.Name", "Organization name is required (150 characters max).");
            await ReloadReadOnlyAsync();
            return Page();
        }

        // Save location.
        await _organizationService.UpdateLocationAsync(Input.Address, Input.Latitude, Input.Longitude);

        // Save notification email.
        await _organizationService.UpdateNotificationEmailAsync(Input.NotificationEmail);

        // Handle Brand Logo upload or removal.
        if (Input.RemoveLogo)
        {
            await _organizationService.UpdateCurrentLogoAsync(null);
        }
        else if (Input.LogoImage is not null && Input.LogoImage.Length > 0)
        {
            var org = await _organizationService.GetCurrentAsync();
            if (org is not null)
            {
                try
                {
                    var logoPath = await _imageStorage.UploadLogoAsync(org.Id, Input.LogoImage);
                    await _organizationService.UpdateCurrentLogoAsync(logoPath);
                }
                catch (CourtImageValidationException ex)
                {
                    ModelState.AddModelError("Input.LogoImage", ex.Message);
                    await ReloadReadOnlyAsync();
                    return Page();
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("Input.LogoImage", $"Logo upload failed: {ex.Message}");
                    await ReloadReadOnlyAsync();
                    return Page();
                }
            }
        }

        // Save payment settings.
        string? qrPath = null;
        if (Input.QRCodeImage is not null && Input.QRCodeImage.Length > 0)
        {
            var org = await _organizationService.GetCurrentAsync();
            if (org is not null)
            {
                try
                {
                    qrPath = await _proofStorage.UploadQRCodeAsync(org.Id, Input.QRCodeImage);
                }
                catch (CourtImageValidationException ex)
                {
                    ModelState.AddModelError("Input.QRCodeImage", ex.Message);
                    await ReloadReadOnlyAsync();
                    return Page();
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("Input.QRCodeImage", $"QR upload failed: {ex.Message}");
                    await ReloadReadOnlyAsync();
                    return Page();
                }
            }
        }

        // Always save payment settings so a QR-code-only upload is also persisted.
        // SavePaymentSettingsAsync returns false only when GCash is enabled but neither
        // an AccountName nor any QR code is available.
        try
        {
            var saved = await _paymentService.SavePaymentSettingsAsync(
                Input.AccountName ?? string.Empty,
                Input.AccountNumber,
                Input.Instructions,
                qrPath, // null means "keep existing path"
                Input.GCashEnabled);

            if (!saved)
            {
                ModelState.AddModelError("Input.AccountName",
                    "Please provide a GCash Account Name or upload a QR code when GCash payments are enabled.");
                await ReloadReadOnlyAsync();
                return Page();
            }
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"Failed to save payment settings: {ex.Message}");
            await ReloadReadOnlyAsync();
            return Page();
        }

        StatusMessage = "Settings saved successfully.";
        return RedirectToPage();


    }

    private async Task ReloadReadOnlyAsync()
    {
        var organization = await _organizationService.GetCurrentAsync();
        if (organization is not null)
        {
            Slug           = organization.Slug;
            Status         = organization.Status;
            CreatedAt      = organization.CreatedAt;
            CurrentLogoUrl = _imageStorage.GetPublicUrl(organization.LogoPath);
        }

        var paySettings = await _paymentService.GetPaymentSettingsAsync();
        if (paySettings is not null)
        {
            CurrentQRCodeUrl = _proofStorage.GetQRCodePublicUrl(paySettings.QRCodeImagePath);
        }
    }

    public class SettingsInput
    {
        // Organization
        [Required(ErrorMessage = "Organization name is required.")]
        [StringLength(150)]
        [Display(Name = "Organization Name")]
        public string Name { get; set; } = string.Empty;

        [StringLength(300)]
        [Display(Name = "Address")]
        public string? Address { get; set; }

        [Range(-90, 90, ErrorMessage = "Latitude must be between -90 and 90.")]
        [Display(Name = "Latitude")]
        public double? Latitude { get; set; }

        [Range(-180, 180, ErrorMessage = "Longitude must be between -180 and 180.")]
        [Display(Name = "Longitude")]
        public double? Longitude { get; set; }

        // Payment
        [StringLength(100)]
        [Display(Name = "GCash Account Name")]
        public string? AccountName { get; set; }

        [StringLength(50)]
        [Display(Name = "GCash Number")]
        public string? AccountNumber { get; set; }

        [StringLength(1000)]
        [Display(Name = "Payment Instructions")]
        public string? Instructions { get; set; }

        [Display(Name = "Enable GCash Payments")]
        public bool GCashEnabled { get; set; } = true;

        [Display(Name = "GCash QR Code Image")]
        public IFormFile? QRCodeImage { get; set; }

        // Notifications
        [StringLength(256)]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        [Display(Name = "Notification Email")]
        public string? NotificationEmail { get; set; }

        // Branding
        [Display(Name = "Brand Logo")]
        public IFormFile? LogoImage { get; set; }

        [Display(Name = "Remove custom logo (revert to default)")]
        public bool RemoveLogo { get; set; }
    }
}
