using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Account;

public class LoginModel : PageModel
{
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantHostParser _hostParser;
    private readonly TenantOptions _tenantOptions;

    public LoginModel(
        SignInManager<IdentityUser> signInManager,
        ITenantContext tenantContext,
        ITenantHostParser hostParser,
        IOptions<TenantOptions> tenantOptions)
    {
        _signInManager = signInManager;
        _tenantContext = tenantContext;
        _hostParser = hostParser;
        _tenantOptions = tenantOptions.Value;
    }

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _signInManager.PasswordSignInAsync(
            Input.Email,
            Input.Password,
            isPersistent: false,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            // If the request is on a tenant subdomain but the tenant did NOT resolve
            // (org is inactive, suspended, or unknown), sign the user back out and show
            // a clear error rather than silently looping back to the login page.
            var host = HttpContext.Request.Host.Host;
            var isOnTenantSubdomain = _hostParser.TryGetTenantSlug(host, _tenantOptions.BaseDomain, out _);

            if (isOnTenantSubdomain && !_tenantContext.IsResolved)
            {
                await _signInManager.SignOutAsync();
                ErrorMessage = "This organization is Inactive or Suspended. Contact the platform administrator.";
                return Page();
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            // If no tenant is resolved (root/platform domain), go to the platform admin
            // org list. If a tenant IS resolved (subdomain), go to the tenant dashboard.
            return _tenantContext.IsResolved
                ? RedirectToPage("/Admin/Index")
                : RedirectToPage("/Admin/Organizations/Index");
        }

        ErrorMessage = "Invalid login attempt.";
        return Page();
    }

    public class LoginInput
    {
        [Required(ErrorMessage = "Please enter your email.")]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your password.")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;
    }
}
