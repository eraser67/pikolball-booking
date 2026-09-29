using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages;

/// <summary>
/// Public self-registration page for court owners / new tenants.
/// Only accessible when PlatformSettings.AllowTenantRegistration = true.
/// Creates the Organization (Active) + owner Identity account, then sends
/// an activation email so the owner can set their password and log in.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting("auth-limit")]
public class JoinUsModel : PageModel
{
    private readonly PlatformSettingsService _platformSettings;
    private readonly IOrganizationService    _orgService;
    private readonly IEmailService           _emailService;
    private readonly IConfiguration          _config;

    public JoinUsModel(
        PlatformSettingsService platformSettings,
        IOrganizationService    orgService,
        IEmailService           emailService,
        IConfiguration          config)
    {
        _platformSettings = platformSettings;
        _orgService       = orgService;
        _emailService     = emailService;
        _config           = config;
    }

    public string PlatformDomain { get; private set; } = "punitbola.tech";
    public string? RegistrationMessage { get; set; }

    [BindProperty]
    public JoinInput Input { get; set; } = new();

    public string? ErrorMessage { get; set; }
    public bool    Success      { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var settings = await _platformSettings.GetAsync();
        if (!settings.AllowTenantRegistration)
            return NotFound();

        PlatformDomain      = _config["Tenant:BaseDomain"] ?? "punitbola.tech";
        RegistrationMessage = settings.RegistrationMessage;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Re-check toggle on POST so disabling mid-flight is respected.
        var settings = await _platformSettings.GetAsync();
        if (!settings.AllowTenantRegistration)
            return NotFound();

        PlatformDomain      = _config["Tenant:BaseDomain"] ?? "punitbola.tech";
        RegistrationMessage = settings.RegistrationMessage;

        if (!ModelState.IsValid)
            return Page();

        var result = await _orgService.CreateAsync(
            Input.VenueName,
            Input.Subdomain.Trim().ToLowerInvariant(),
            Input.OwnerName,
            Input.OwnerEmail);

        if (!result.Success)
        {
            foreach (var (field, msg) in result.Errors)
                ModelState.AddModelError($"Input.{field}", msg);
            return Page();
        }

        // Send activation email if the owner account was freshly created.
        if (result.OwnerAccountCreated && result.OwnerActivationToken is not null)
        {
            var activationUrl = Url.Page(
                "/Account/Activate",
                null,
                new { userId = result.OwnerUserId, token = result.OwnerActivationToken },
                Request.Scheme)!;

            var htmlBody = $"""
                <div style="font-family:Inter,sans-serif;max-width:560px;margin:0 auto;">
                  <h2 style="color:#146c43;">Welcome to Pikolball! 🎉</h2>
                  <p>Your venue <strong>{System.Net.WebUtility.HtmlEncode(Input.VenueName)}</strong> has been registered successfully.</p>
                  <p>Click the button below to activate your account and set your password:</p>
                  <a href="{activationUrl}" style="display:inline-block;background:#198754;color:#fff;padding:12px 28px;border-radius:8px;text-decoration:none;font-weight:600;">
                      Activate My Account
                  </a>
                  <p style="color:#888;margin-top:24px;font-size:0.875rem;">
                      If you didn't register on Pikolball, you can safely ignore this email.
                  </p>
                </div>
                """;

            await _emailService.SendAsync(
                toAddress: Input.OwnerEmail,
                toName:    Input.OwnerName,
                subject:   $"Activate your Pikolball account — {Input.VenueName}",
                htmlBody:  htmlBody);
        }

        Success = true;
        return Page();
    }

    public class JoinInput
    {
        [Required(ErrorMessage = "Venue name is required.")]
        [StringLength(150, ErrorMessage = "Venue name must be at most 150 characters.")]
        [Display(Name = "Venue / Club Name")]
        public string VenueName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Subdomain is required.")]
        [StringLength(63, MinimumLength = 3, ErrorMessage = "Subdomain must be 3–63 characters.")]
        [RegularExpression(@"^[a-z0-9][a-z0-9\-]*[a-z0-9]$",
            ErrorMessage = "Only lowercase letters, numbers, and hyphens are allowed (no leading/trailing hyphens).")]
        [Display(Name = "Subdomain (e.g. myvenue)")]
        public string Subdomain { get; set; } = string.Empty;

        [Required(ErrorMessage = "Owner name is required.")]
        [StringLength(100, ErrorMessage = "Name must be at most 100 characters.")]
        [Display(Name = "Your Full Name")]
        public string OwnerName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Owner email is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [Display(Name = "Your Email Address")]
        public string OwnerEmail { get; set; } = string.Empty;
    }
}
