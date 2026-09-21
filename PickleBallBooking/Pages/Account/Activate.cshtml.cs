using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PickleBallBooking.Data;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Account;

/// <summary>
/// Phase 23: owner account activation / credential setup.
///
/// When the platform admin creates an organization with a brand-new owner email, an
/// Identity user is created WITHOUT a password and is NOT email-confirmed. The
/// activation link (built from an Identity email-confirmation token) is surfaced to
/// the platform admin for testing; in a later phase the email pipeline will deliver
/// the same link to the owner. Visiting it confirms the email and lets the owner
/// choose their own password - no permanent password is ever shown or stored in the
/// admin UI, and no custom authentication system is introduced.
///
/// Anonymous access is required (the owner is not signed in yet); the security of the
/// operation rests entirely on the single-use Identity token.
/// </summary>
[AllowAnonymous]
public class ActivateModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly TenantOptions _tenantOptions;

    public ActivateModel(
        UserManager<IdentityUser> userManager,
        ApplicationDbContext context,
        IOptions<TenantOptions> tenantOptions)
    {
        _userManager = userManager;
        _context = context;
        _tenantOptions = tenantOptions.Value;
    }

    [BindProperty]
    public ActivateInput Input { get; set; } = new();

    public string? StatusMessage { get; set; }

    public bool LinkInvalid { get; set; }

    public bool Completed { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The subdomain login URL for the owner's org, shown after activation.
    /// Falls back to the root login page if the org can't be determined.
    /// </summary>
    public string LoginUrl { get; set; } = "/Account/Login";

    public async Task<IActionResult> OnGetAsync(string? userId, string? token)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
        {
            LinkInvalid = true;
            return Page();
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            LinkInvalid = true;
            return Page();
        }

        // Keep the token in the form so OnPost can consume it once.
        Input.UserId = userId;
        Input.Token = token;

        if (user.EmailConfirmed && !await _userManager.HasPasswordAsync(user))
        {
            StatusMessage = "Your email is confirmed. Set a password to finish activating your account.";
        }
        else if (user.EmailConfirmed)
        {
            StatusMessage = "Your account is already active. You can sign in, or set a new password below.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Input.UserId) || string.IsNullOrWhiteSpace(Input.Token))
        {
            LinkInvalid = true;
            return Page();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await _userManager.FindByIdAsync(Input.UserId);
        if (user is null)
        {
            LinkInvalid = true;
            return Page();
        }

        // Confirm the email once using the single-use token.
        if (!user.EmailConfirmed)
        {
            var confirmResult = await _userManager.ConfirmEmailAsync(user, Input.Token);
            if (!confirmResult.Succeeded)
            {
                LinkInvalid = true;
                return Page();
            }
        }

        // Establish the owner's own credentials. This is the only place a password is
        // chosen for an invited account, and it is chosen by the owner themselves.
        if (await _userManager.HasPasswordAsync(user))
        {
            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
            var resetResult = await _userManager.ResetPasswordAsync(user, resetToken, Input.Password);
            if (!resetResult.Succeeded)
            {
                ErrorMessage = string.Join(" ", resetResult.Errors.Select(e => e.Description));
                return Page();
            }
        }
        else
        {
            var addResult = await _userManager.AddPasswordAsync(user, Input.Password);
            if (!addResult.Succeeded)
            {
                ErrorMessage = string.Join(" ", addResult.Errors.Select(e => e.Description));
                return Page();
            }
        }

        Completed = true;

        // Build a subdomain login URL so the owner lands on the correct tenant.
        var userId = Input.UserId;
        var membership = await _context.OrganizationMembers
            .Where(m => m.UserId == userId)
            .Join(_context.Organizations,
                m => m.OrganizationId,
                o => o.Id,
                (m, o) => o.Slug)
            .FirstOrDefaultAsync();

        if (!string.IsNullOrEmpty(membership))
        {
            LoginUrl = $"{Request.Scheme}://{membership}.{_tenantOptions.BaseDomain}:{Request.Host.Port}/Account/Login";
        }

        return Page();
    }

    public class ActivateInput
    {
        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public string Token { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please choose a password.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least {2} characters.")]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
