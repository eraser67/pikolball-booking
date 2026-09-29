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

    /// <summary>Current custom hero image public URL (for display).</summary>
    public string? CurrentHeroImageUrl { get; set; }

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
        Input.TelegramChatId    = organization.TelegramChatId;

        // Branding
        Input.Tagline             = organization.Tagline;
        Input.AboutText           = organization.AboutText;
        Input.PrimaryColorHex     = organization.PrimaryColorHex;
        Input.FacebookUrl         = organization.FacebookUrl;
        Input.InstagramUrl        = organization.InstagramUrl;
        Input.TwitterUrl          = organization.TwitterUrl;
        Input.ShowActivitiesOnHome = organization.ShowActivitiesOnHome;

        // Section toggles
        Input.ShowHowItWorksSection = organization.ShowHowItWorksSection;
        Input.ShowWhyUsSection      = organization.ShowWhyUsSection;
        Input.ShowFaqSection        = organization.ShowFaqSection;
        Input.ShowLocationSection   = organization.ShowLocationSection;

        // Amenities
        Input.ShowAmenitiesSection = organization.ShowAmenitiesSection;
        Input.SelectedAmenities    = organization.AmenitiesKeys
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList() ?? new();

        // Opening hours + announcement
        Input.OpeningHours           = organization.OpeningHours;
        Input.AnnouncementText       = organization.AnnouncementText;
        Input.ShowAnnouncementBanner = organization.ShowAnnouncementBanner;

        CurrentLogoUrl      = _imageStorage.GetPublicUrl(organization.LogoPath);
        CurrentHeroImageUrl = _imageStorage.GetPublicUrl(organization.HeroImagePath);

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

        // Save notifications (email + Telegram).
        await _organizationService.UpdateNotificationEmailAsync(Input.NotificationEmail);
        await _organizationService.UpdateTelegramChatIdAsync(Input.TelegramChatId);

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

        // Handle Hero Image upload or removal.
        if (Input.RemoveHeroImage)
        {
            await _organizationService.UpdateCurrentHeroImageAsync(null);
        }
        else if (Input.HeroImage is not null && Input.HeroImage.Length > 0)
        {
            var org = await _organizationService.GetCurrentAsync();
            if (org is not null)
            {
                try
                {
                    var heroPath = await _imageStorage.UploadHeroImageAsync(org.Id, Input.HeroImage);
                    await _organizationService.UpdateCurrentHeroImageAsync(heroPath);
                }
                catch (CourtImageValidationException ex)
                {
                    ModelState.AddModelError("Input.HeroImage", ex.Message);
                    await ReloadReadOnlyAsync();
                    return Page();
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("Input.HeroImage", $"Hero image upload failed: {ex.Message}");
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

        // Save branding settings.
        await _organizationService.UpdateBrandingAsync(
            Input.Tagline,
            Input.AboutText,
            Input.PrimaryColorHex,
            Input.FacebookUrl,
            Input.InstagramUrl,
            Input.TwitterUrl,
            Input.ShowActivitiesOnHome,
            Input.ShowHowItWorksSection,
            Input.ShowWhyUsSection,
            Input.ShowFaqSection,
            Input.ShowLocationSection,
            Input.SelectedAmenities,
            Input.ShowAmenitiesSection,
            Input.OpeningHours,
            Input.AnnouncementText,
            Input.ShowAnnouncementBanner);

        StatusMessage = "Settings saved successfully.";
        return RedirectToPage();


    }

    private async Task ReloadReadOnlyAsync()
    {
        var organization = await _organizationService.GetCurrentAsync();
        if (organization is not null)
        {
            Slug             = organization.Slug;
            Status           = organization.Status;
            CreatedAt        = organization.CreatedAt;
            CurrentLogoUrl   = _imageStorage.GetPublicUrl(organization.LogoPath);
            CurrentHeroImageUrl = _imageStorage.GetPublicUrl(organization.HeroImagePath);
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

        [StringLength(100)]
        [Display(Name = "Telegram Chat ID")]
        public string? TelegramChatId { get; set; }

        // Branding
        [MaxLength(200)]
        [Display(Name = "Tagline")]
        public string? Tagline { get; set; }

        [MaxLength(1000)]
        [Display(Name = "About / Welcome Text")]
        public string? AboutText { get; set; }

        [MaxLength(7)]
        [RegularExpression(@"^(#[0-9A-Fa-f]{6})?$", ErrorMessage = "Enter a 6-digit hex color, e.g. #16a34a, or leave blank.")]
        [Display(Name = "Primary Brand Color (hex)")]
        public string? PrimaryColorHex { get; set; }

        [MaxLength(300)]
        [Url(ErrorMessage = "Enter a valid URL.")]
        [Display(Name = "Facebook URL")]
        public string? FacebookUrl { get; set; }

        [MaxLength(300)]
        [Url(ErrorMessage = "Enter a valid URL.")]
        [Display(Name = "Instagram URL")]
        public string? InstagramUrl { get; set; }

        [MaxLength(300)]
        [Url(ErrorMessage = "Enter a valid URL.")]
        [Display(Name = "X / Twitter URL")]
        public string? TwitterUrl { get; set; }

        [Display(Name = "Show upcoming Activities on homepage")]
        public bool ShowActivitiesOnHome { get; set; } = false;

        // Section Visibility
        [Display(Name = "Show \"How It Works\" section")]
        public bool ShowHowItWorksSection { get; set; } = true;

        [Display(Name = "Show \"Why Us\" section")]
        public bool ShowWhyUsSection { get; set; } = true;

        [Display(Name = "Show FAQ section")]
        public bool ShowFaqSection { get; set; } = true;

        [Display(Name = "Show Location & Map section")]
        public bool ShowLocationSection { get; set; } = true;

        // Amenities
        [Display(Name = "Show Amenities section on homepage")]
        public bool ShowAmenitiesSection { get; set; } = false;

        public List<string> SelectedAmenities { get; set; } = new();

        // Opening Hours
        [MaxLength(200)]
        [Display(Name = "Opening Hours")]
        public string? OpeningHours { get; set; }

        // Announcement Banner
        [MaxLength(300)]
        [Display(Name = "Announcement Text")]
        public string? AnnouncementText { get; set; }

        [Display(Name = "Show announcement banner")]
        public bool ShowAnnouncementBanner { get; set; } = false;

        // Branding images
        [Display(Name = "Brand Logo")]
        public IFormFile? LogoImage { get; set; }

        [Display(Name = "Remove custom logo (revert to default)")]
        public bool RemoveLogo { get; set; }

        [Display(Name = "Hero Image")]
        public IFormFile? HeroImage { get; set; }

        [Display(Name = "Remove custom hero image (revert to default)")]
        public bool RemoveHeroImage { get; set; }
    }
}
