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

        // Always send an email — new accounts get an activation link,
        // existing confirmed accounts get a "venue linked" notification.
        string emailSubject;
        string emailHtml;
        var venueName = System.Net.WebUtility.HtmlEncode(Input.VenueName);
        var baseDomain = _config["Tenant:BaseDomain"] ?? PlatformDomain;
        var portPart   = Request.Host.Port.HasValue && Request.Host.Port.Value != 80 && Request.Host.Port.Value != 443
                         ? $":{Request.Host.Port.Value}" : string.Empty;
        var loginUrl   = $"{Request.Scheme}://{Input.Subdomain.Trim().ToLowerInvariant()}.{baseDomain}{portPart}/Account/Login";

        if (result.OwnerAccountCreated && result.OwnerActivationToken is not null)
        {
            // Brand-new Identity user — must confirm email + set password first.
            var activationUrl = Url.Page(
                "/Account/Activate", null,
                new { userId = result.OwnerUserId, token = result.OwnerActivationToken },
                Request.Scheme)!;

            emailSubject = $"Activate your Punit Bola account — {Input.VenueName}";
            emailHtml = $"""
                <div style="font-family:Inter,sans-serif;max-width:560px;margin:0 auto;padding:24px;">
                  <h2 style="color:#146c43;margin-bottom:4px;">Welcome to Punit Bola! 🎉</h2>
                  <p style="color:#555;">Your venue <strong>{venueName}</strong> has been registered successfully.</p>
                  <p style="color:#555;">Click the button below to activate your account and set your password:</p>
                  <a href="{activationUrl}"
                     style="display:inline-block;background:#198754;color:#fff;padding:13px 28px;border-radius:8px;text-decoration:none;font-weight:700;font-size:1rem;margin:8px 0;">
                    Activate My Account
                  </a>
                  <p style="color:#888;margin-top:24px;font-size:0.85rem;">
                    If you didn't register on Punit Bola, you can safely ignore this email.
                  </p>
                </div>
                """;
        }
        else
        {
            // Existing confirmed account — venue linked, send login link directly.
            emailSubject = $"Your new venue is ready — {Input.VenueName}";
            emailHtml = $"""
                <div style="font-family:Inter,sans-serif;max-width:560px;margin:0 auto;padding:24px;">
                  <h2 style="color:#146c43;margin-bottom:4px;">Your venue is live! 🎉</h2>
                  <p style="color:#555;">
                    Your venue <strong>{venueName}</strong> has been registered on Punit Bola
                    and linked to your existing account (<strong>{System.Net.WebUtility.HtmlEncode(Input.OwnerEmail)}</strong>).
                  </p>
                  <p style="color:#555;">Click the button below to log in and start setting up your venue:</p>
                  <a href="{loginUrl}"
                     style="display:inline-block;background:#198754;color:#fff;padding:13px 28px;border-radius:8px;text-decoration:none;font-weight:700;font-size:1rem;margin:8px 0;">
                    Go to My Venue Dashboard
                  </a>
                  <p style="color:#888;margin-top:24px;font-size:0.85rem;">
                    Your venue URL: <a href="{loginUrl}" style="color:#146c43;">{Input.Subdomain.Trim().ToLowerInvariant()}.{baseDomain}</a>
                  </p>
                </div>
                """;
        }

        await _emailService.SendAsync(
            toAddress: Input.OwnerEmail,
            toName:    Input.OwnerName,
            subject:   emailSubject,
            htmlBody:  emailHtml);

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
