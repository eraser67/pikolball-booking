using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Account;

/// <summary>
/// Phase 31: self-registration page for customers / players.
///
/// Creates a platform-level Identity account with the Customer role.
/// After successful registration the user is signed in and redirected
/// to the customer dashboard (/Customer/Dashboard).
///
/// Anonymous access is required; authenticated users (admins or customers)
/// are redirected away from this page before the form is shown.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting("auth-limit")]
public class RegisterModel : PageModel
{
    private readonly CustomerRegistrationService _registrationService;
    private readonly SignInManager<IdentityUser> _signInManager;

    public RegisterModel(
        CustomerRegistrationService registrationService,
        SignInManager<IdentityUser> signInManager)
    {
        _registrationService = registrationService;
        _signInManager       = signInManager;
    }

    [BindProperty]
    public RegisterInput Input { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public IActionResult OnGet()
    {
        // Already logged-in users do not need to register again.
        if (User.Identity?.IsAuthenticated == true)
        {
            if (User.IsInRole(PlatformRoles.Customer))
            {
                return RedirectToPage("/Customer/Dashboard");
            }
            return RedirectToPage("/Admin/Index");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var (user, errors) = await _registrationService.RegisterAsync(
            Input.FullName,
            Input.Email,
            Input.Mobile,
            Input.Password);

        if (user is null)
        {
            foreach (var error in errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return Page();
        }

        // Sign the user in immediately after registration.
        await _signInManager.SignInAsync(user, isPersistent: false);

        return RedirectToPage("/Customer/Dashboard");
    }

    public class RegisterInput
    {
        [Required(ErrorMessage = "Please enter your full name.")]
        [StringLength(100, ErrorMessage = "Name must be at most 100 characters.")]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your email address.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your mobile number.")]
        [Phone(ErrorMessage = "Please enter a valid mobile number.")]
        [StringLength(20, ErrorMessage = "Mobile number must be at most 20 characters.")]
        [Display(Name = "Mobile Number")]
        public string Mobile { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please choose a password.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least {2} characters.")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm Password")]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
