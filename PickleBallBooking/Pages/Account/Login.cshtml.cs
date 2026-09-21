using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.RateLimiting;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Account;

[EnableRateLimiting("auth-limit")]
public class LoginModel : PageModel
{
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantHostParser _hostParser;
    private readonly TenantOptions _tenantOptions;

    public LoginModel(
        SignInManager<IdentityUser> signInManager,
        UserManager<IdentityUser> userManager,
        ApplicationDbContext context,
        ITenantContext tenantContext,
        ITenantHostParser hostParser,
        IOptions<TenantOptions> tenantOptions)
    {
        _signInManager = signInManager;
        _userManager   = userManager;
        _context       = context;
        _tenantContext = tenantContext;
        _hostParser    = hostParser;
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

            // Check if the authenticated user is a PlatformAdmin
            var user = await _userManager.FindByEmailAsync(Input.Email);
            var isPlatformAdmin = user is not null && await _userManager.IsInRoleAsync(user, PlatformRoles.PlatformAdmin);

            // 1. If tenant is resolved on this request (user is on their subdomain):
            if (_tenantContext.IsResolved)
            {
                return RedirectToPage("/Admin/Index");
            }

            // 2. If user is a PlatformAdmin on the root/apex domain:
            if (isPlatformAdmin)
            {
                return RedirectToPage("/Admin/Organizations/Index");
            }

            // 3. User is an organization owner/admin/staff logging in from the apex/root domain:
            // Locate their primary active organization and redirect them to their subdomain dashboard.
            if (user is not null)
            {
                var tenantSlug = await _context.OrganizationMembers
                    .Where(m => m.UserId == user.Id)
                    .Join(_context.Organizations,
                        m => m.OrganizationId,
                        o => o.Id,
                        (m, o) => new { o.Slug, o.Status })
                    .Where(x => x.Status == OrganizationStatus.Active)
                    .Select(x => x.Slug)
                    .FirstOrDefaultAsync();

                if (!string.IsNullOrEmpty(tenantSlug))
                {
                    var portPart = Request.Host.Port.HasValue && Request.Host.Port.Value != 80 && Request.Host.Port.Value != 443
                        ? $":{Request.Host.Port.Value}"
                        : string.Empty;

                    var tenantAdminUrl = $"{Request.Scheme}://{tenantSlug}.{_tenantOptions.BaseDomain}{portPart}/Admin/Index";
                    return Redirect(tenantAdminUrl);
                }
            }

            // 4. User is neither a PlatformAdmin nor a member of an active organization:
            await _signInManager.SignOutAsync();
            ErrorMessage = "You do not have administrative access to an active organization. Please contact your administrator.";
            return Page();
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
