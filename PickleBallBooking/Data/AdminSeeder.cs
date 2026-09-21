using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Data;

/// <summary>
/// Seeds a single initial administrator account from configuration.
/// Configure "Admin:Email" and "Admin:Password" via user-secrets or environment
/// variables (e.g. Admin__Email / Admin__Password) - never commit real credentials.
///
/// Phase 20.5: also ensures the first ("Pikolball") organization exists and that the
/// seeded administrator is a member of it (as OrganizationOwner), so the new
/// multi-tenant foundation has at least one organization owner without implementing
/// any tenant resolution/filtering.
///
/// Phase 23: the seeded administrator is also granted the platform-admin Identity
/// role (<see cref="PlatformRoles.PlatformAdmin"/>), which authorizes platform-level
/// organization management. The role is created if missing and the assignment is
/// idempotent.
/// </summary>
public static class AdminSeeder
{
    public static async Task SeedAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var adminEmail = configuration["Admin:Email"];
        var adminPassword = configuration["Admin:Password"];

        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
        {
            return;
        }

                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(AdminSeeder));

        // Phase 21: bind the scope's tenant context before touching tenant-owned data.
        // (AdminSeeder only writes Organizations/OrganizationMembers, which are not
        // tenant-filtered, but binding keeps the scope consistent and future-proof.)
        var organizationId = await scope.RequireResolvedTenantAsync();

        // Phase 23: ensure the platform-admin role exists.
        if (!await roleManager.RoleExistsAsync(PlatformRoles.PlatformAdmin))
        {
            await roleManager.CreateAsync(new IdentityRole(PlatformRoles.PlatformAdmin));
            logger.LogInformation("Created platform role {Role}.", PlatformRoles.PlatformAdmin);
    }

        var existingAdmin = await userManager.FindByEmailAsync(adminEmail);
        if (existingAdmin is not null)
        {
            await EnsureMembershipAsync(context, logger, organizationId, existingAdmin.Id);
            await EnsurePlatformAdminRoleAsync(userManager, logger, existingAdmin);
            return;
}

        var adminUser = new IdentityUser
    {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(adminUser, adminPassword);
        if (!result.Succeeded)
        {
            logger.LogError("Failed to seed admin user: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
            return;
        }

        await EnsureMembershipAsync(context, logger, organizationId, adminUser.Id);
        await EnsurePlatformAdminRoleAsync(userManager, logger, adminUser);
    }

    /// <summary>
    /// Ensures the given Identity user belongs to the organization as its owner.
    /// Idempotent: a membership row is only added when one does not already exist.
    /// </summary>
    private static async Task EnsureMembershipAsync(
        ApplicationDbContext context,
        ILogger logger,
        int organizationId,
        string userId)
        {
        var alreadyMember = await context.OrganizationMembers
            .AnyAsync(m => m.OrganizationId == organizationId && m.UserId == userId);

        if (alreadyMember)
        {
            return;
        }
        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organizationId,
            UserId = userId,
            Role = OrganizationRole.OrganizationOwner,
            CreatedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync();
        logger.LogInformation("Linked administrator to organization {OrganizationId} as OrganizationOwner.", organizationId);
    }

    /// <summary>
    /// Grants the platform-admin Identity role to the seeded administrator,
    /// idempotently.
    /// </summary>
    private static async Task EnsurePlatformAdminRoleAsync(
        UserManager<IdentityUser> userManager,
        ILogger logger,
        IdentityUser user)
    {
        if (await userManager.IsInRoleAsync(user, PlatformRoles.PlatformAdmin))
        {
            return;
}

        var result = await userManager.AddToRoleAsync(user, PlatformRoles.PlatformAdmin);
        if (result.Succeeded)
        {
            logger.LogInformation("Granted platform role {Role} to {Email}.", PlatformRoles.PlatformAdmin, user.Email);
        }
        else
        {
            logger.LogWarning(
                "Could not grant platform role {Role} to {Email}: {Errors}",
                PlatformRoles.PlatformAdmin,
                user.Email,
                string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }
}

