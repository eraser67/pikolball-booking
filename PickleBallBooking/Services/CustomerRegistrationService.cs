using Microsoft.AspNetCore.Identity;

namespace PickleBallBooking.Services;

/// <summary>
/// Phase 31: encapsulates all identity operations needed to self-register a
/// new customer / player account.
///
/// Responsibilities:
///   - Ensure the Customer Identity role exists (idempotent).
///   - Create the IdentityUser with email-confirmed = true (no email verification
///     in Phase 31; add optional email confirmation in a later phase).
///   - Assign the Customer role so the login flow can distinguish customers from
///     admin users.
///
/// Customers are platform-level accounts. They have NO OrganizationMember rows
/// and therefore do not resolve a tenant. They use /Customer/* pages only.
///
/// This service does NOT sign the user in; the caller (Register page) signs in
/// after a successful registration using SignInManager.
/// </summary>
public sealed class CustomerRegistrationService
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ILogger<CustomerRegistrationService> _logger;

    public CustomerRegistrationService(
        UserManager<IdentityUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ILogger<CustomerRegistrationService> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new customer account and assigns the Customer role.
    /// </summary>
    /// <param name="fullName">Display name stored as a claim.</param>
    /// <param name="email">Email address used as both UserName and Email.</param>
    /// <param name="mobile">Mobile number stored as a claim.</param>
    /// <param name="password">Plain-text password; Identity hashes it.</param>
    /// <returns>
    /// The created <see cref="IdentityUser"/> on success, or <c>null</c> with
    /// errors populated on failure.
    /// </returns>
    public async Task<(IdentityUser? User, IEnumerable<IdentityError> Errors)> RegisterAsync(
        string fullName,
        string email,
        string mobile,
        string password)
    {
        // 1. Ensure the Customer role exists (idempotent startup task).
        await EnsureCustomerRoleAsync();

        // 2. Create the Identity user.
        var user = new IdentityUser
        {
            UserName       = email,
            Email          = email,
            PhoneNumber    = mobile,
            // Phase 31: email confirmation is skipped; the player is immediately active.
            // Add optional email verification in a future phase (Phase 36 notifications).
            EmailConfirmed = true
        };

        var createResult = await _userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            _logger.LogWarning(
                "Customer registration failed for {Email}: {Errors}",
                email,
                string.Join(", ", createResult.Errors.Select(e => e.Description)));
            return (null, createResult.Errors);
        }

        // 3. Store the full name as a claim so it is available without a DB round-trip.
        //    Phase 32 (PlayerProfile) adds the full profile entity.
        var claimResult = await _userManager.AddClaimsAsync(user, new[]
        {
            new System.Security.Claims.Claim("fullName", fullName),
            new System.Security.Claims.Claim("mobile",   mobile)
        });

        if (!claimResult.Succeeded)
        {
            // Non-fatal: the account is created; log the failure and continue.
            _logger.LogWarning(
                "Could not add profile claims for {Email}: {Errors}",
                email,
                string.Join(", ", claimResult.Errors.Select(e => e.Description)));
        }

        // 4. Assign the Customer role.
        var roleResult = await _userManager.AddToRoleAsync(user, PlatformRoles.Customer);
        if (!roleResult.Succeeded)
        {
            _logger.LogWarning(
                "Could not assign Customer role to {Email}: {Errors}",
                email,
                string.Join(", ", roleResult.Errors.Select(e => e.Description)));
        }

        _logger.LogInformation("Customer account created for {Email}.", email);
        return (user, Enumerable.Empty<IdentityError>());
    }

    private async Task EnsureCustomerRoleAsync()
    {
        if (!await _roleManager.RoleExistsAsync(PlatformRoles.Customer))
        {
            await _roleManager.CreateAsync(new IdentityRole(PlatformRoles.Customer));
            _logger.LogInformation("Created platform role '{Role}'.", PlatformRoles.Customer);
        }
    }
}
