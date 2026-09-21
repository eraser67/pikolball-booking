using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using Xunit;

namespace PickleBallBooking.Tests;

/// <summary>
/// Phase 23: OrganizationService unit tests.
///
/// Covers:
///  - Organization creation (success paths)
///  - Slug validation: invalid format, reserved, duplicate
///  - Owner account creation for new email
///  - Existing Identity user reuse (no duplicate account)
///  - Tenant-scoped operations: GetCurrentAsync, RenameCurrentAsync, GetCurrentMembersAsync
///  - Hostname resolution after creation (via TenantResolver)
///  - Inactive organization inaccessible through subdomain
/// </summary>
public class OrganizationServiceTests
{
    private const string BaseDomain = "punitbola.com";

    // -------------------------------------------------------------------------
    // Factory helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds an OrganizationService backed by an EF InMemory context with an optional
    /// resolved tenant. When tenantId is null the context has NO resolved tenant.
    /// </summary>
    private static (ApplicationDbContext Context, OrganizationService Service) CreateService(
        string? databaseName = null, int? tenantId = null)
    {
        var dbName = databaseName ?? Guid.NewGuid().ToString();
        var context = tenantId.HasValue
            ? TestDbContextFactory.CreateInMemory(dbName, tenantId.Value)
            : TestDbContextFactory.CreateWithoutTenant(dbName);

        var tenantContext = new TenantContext { OrganizationId = tenantId };

        var userStore = new InMemoryUserStore(context);

        // Build UserManager manually with a stub token provider so
        // GenerateEmailConfirmationTokenAsync works without DataProtection.
        var options = Microsoft.Extensions.Options.Options.Create(new IdentityOptions());
        var tokenProviders = new Dictionary<string, IUserTwoFactorTokenProvider<IdentityUser>>
        {
            // "Default" is the provider name used by GenerateEmailConfirmationTokenAsync.
            [TokenOptions.DefaultProvider] = new StubTokenProvider()
        };
        var userManager = new UserManager<IdentityUser>(
            userStore,
            options,
            new PasswordHasher<IdentityUser>(),
            Array.Empty<IUserValidator<IdentityUser>>(),
            Array.Empty<IPasswordValidator<IdentityUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,   // IServiceProvider — not needed in unit tests
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<UserManager<IdentityUser>>());

        // Register the stub token provider under the "Default" name.
        userManager.RegisterTokenProvider(TokenOptions.DefaultProvider, new StubTokenProvider());

        var service = new OrganizationService(
            context,
            userManager,
            tenantContext,
            new ReservedSlugs());

        return (context, service);
    }


    // -------------------------------------------------------------------------
    // Creation -- success
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_NewOrg_NewOwnerEmail_Succeeds()
    {
        var (_, svc) = CreateService();

        var result = await svc.CreateAsync(
            "Cebu Pickleball Club",
            "cebupickleball",
            "Juan Dela Cruz",
            "juan@example.com");

        Assert.True(result.Success,
            string.Join(", ", result.Errors.Select(e => $"{e.Key}:{e.Value}")));
        Assert.NotNull(result.Organization);
        Assert.Equal("Cebu Pickleball Club", result.Organization.Name);
        Assert.Equal("cebupickleball", result.Organization.Slug);
        Assert.Equal(OrganizationStatus.Active, result.Organization.Status);
        Assert.True(result.OwnerAccountCreated);
        Assert.NotNull(result.OwnerUserId);
        // Activation token is generated for a fresh owner account.
        Assert.NotNull(result.OwnerActivationToken);
    }

    [Fact]
    public async Task CreateAsync_OrganizationAndOwnerMembership_ArePersisted()
    {
        var (ctx, svc) = CreateService();

        var result = await svc.CreateAsync(
            "Davao Pickleball",
            "davaopickleball",
            "Maria Santos",
            "maria@example.com");

        Assert.True(result.Success);

        var org = await ctx.Organizations.SingleAsync(o => o.Slug == "davaopickleball");
        Assert.Equal("Davao Pickleball", org.Name);
        Assert.Equal(OrganizationStatus.Active, org.Status);

        var member = await ctx.OrganizationMembers
            .SingleAsync(m => m.OrganizationId == org.Id && m.UserId == result.OwnerUserId);
        Assert.Equal(OrganizationRole.OrganizationOwner, member.Role);
    }

    [Fact]
    public async Task CreateAsync_SlugIsNormalized_ToLowercase()
    {
        var (_, svc) = CreateService();

        var result = await svc.CreateAsync("Test Org", "  CEBUCLUB  ", "Owner", "owner@example.com");

        Assert.True(result.Success);
        Assert.Equal("cebuclub", result.Organization!.Slug);
    }

    // -------------------------------------------------------------------------
    // Creation -- existing Identity user reuse
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_ExistingEmail_ReusesUser_DoesNotCreateDuplicate()
    {
        var dbName = Guid.NewGuid().ToString();
        var (_, svc1) = CreateService(dbName);
        var first = await svc1.CreateAsync("First Org", "firstorg", "Owner", "shared@example.com");
        Assert.True(first.Success);
        Assert.True(first.OwnerAccountCreated);

        var (_, svc2) = CreateService(dbName);
        var second = await svc2.CreateAsync("Second Org", "secondorg", "Owner", "shared@example.com");

        Assert.True(second.Success);
        Assert.False(second.OwnerAccountCreated, "Existing user should be reused, not created.");
        Assert.Null(second.OwnerActivationToken);
        Assert.Equal(first.OwnerUserId, second.OwnerUserId);
    }

    [Fact]
    public async Task CreateAsync_ExistingEmail_OtherMemberships_ArePreserved()
    {
        var dbName = Guid.NewGuid().ToString();
        var (_, svc1) = CreateService(dbName);
        var first = await svc1.CreateAsync("Org A", "orga", "Owner", "multi@example.com");
        Assert.True(first.Success);

        var (ctx2, svc2) = CreateService(dbName);
        var second = await svc2.CreateAsync("Org B", "orgb", "Owner", "multi@example.com");
        Assert.True(second.Success);

        var memberships = await ctx2.OrganizationMembers
            .Where(m => m.UserId == first.OwnerUserId)
            .ToListAsync();
        Assert.Equal(2, memberships.Count);
    }

    // -------------------------------------------------------------------------
    // Slug validation
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_EmptySlug_ReturnsSlugError(string slug)
    {
        var (_, svc) = CreateService();
        var result = await svc.CreateAsync("Org", slug, "Owner", "owner@example.com");
        Assert.False(result.Success);
        Assert.True(result.Errors.ContainsKey("Slug"), $"Expected Slug error for '{slug}'.");
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("has.dot")]
    [InlineData("has_underscore")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    public async Task CreateAsync_InvalidSlugFormat_ReturnsSlugError(string slug)
    {
        var (_, svc) = CreateService();
        var result = await svc.CreateAsync("Org", slug, "Owner", "owner@example.com");
        Assert.False(result.Success);
        Assert.True(result.Errors.ContainsKey("Slug"), $"Expected Slug error for '{slug}'.");
    }

    [Theory]
    [InlineData("www")]
    [InlineData("admin")]
    [InlineData("api")]
    [InlineData("app")]
    [InlineData("mail")]
    [InlineData("support")]
    [InlineData("billing")]
    [InlineData("punitbola")]
    public async Task CreateAsync_ReservedSlug_ReturnsSlugError(string slug)
    {
        var (_, svc) = CreateService();
        var result = await svc.CreateAsync("Org", slug, "Owner", "owner@example.com");
        Assert.False(result.Success);
        Assert.True(result.Errors.ContainsKey("Slug"), $"Expected reserved-slug error for '{slug}'.");
    }

    [Fact]
    public async Task CreateAsync_DuplicateSlug_ReturnsSlugError()
    {
        var dbName = Guid.NewGuid().ToString();
        var (_, svc1) = CreateService(dbName);
        var first = await svc1.CreateAsync("Org One", "uniqueslug", "Owner", "owner1@example.com");
        Assert.True(first.Success);

        var (_, svc2) = CreateService(dbName);
        var second = await svc2.CreateAsync("Org Two", "uniqueslug", "Owner", "owner2@example.com");
        Assert.False(second.Success);
        Assert.True(second.Errors.ContainsKey("Slug"));
    }

    // -------------------------------------------------------------------------
    // Name and email validation
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_EmptyName_ReturnsNameError(string name)
    {
        var (_, svc) = CreateService();
        var result = await svc.CreateAsync(name, "validslug", "Owner", "owner@example.com");
        Assert.False(result.Success);
        Assert.True(result.Errors.ContainsKey("Name"));
    }

    [Theory]
    [InlineData("notanemail")]
    [InlineData("no-at-sign")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_InvalidEmail_ReturnsOwnerEmailError(string email)
    {
        var (_, svc) = CreateService();
        var result = await svc.CreateAsync("Org", "validslug", "Owner", email);
        Assert.False(result.Success);
        Assert.True(result.Errors.ContainsKey("OwnerEmail"), $"Expected OwnerEmail error for '{email}'.");
    }

    // -------------------------------------------------------------------------
    // GetAllAsync / SetStatusAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAllAsync_ReturnsOrgsNewestFirst()
    {
        var dbName = Guid.NewGuid().ToString();
        var (ctx, svc) = CreateService(dbName);

        ctx.Organizations.AddRange(
            new Organization { Name = "Old", Slug = "old", Status = OrganizationStatus.Active, CreatedAt = DateTime.UtcNow.AddDays(-2), UpdatedAt = DateTime.UtcNow },
            new Organization { Name = "New", Slug = "new", Status = OrganizationStatus.Active, CreatedAt = DateTime.UtcNow.AddDays(-1), UpdatedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var orgs = await svc.GetAllAsync();

        Assert.Equal(2, orgs.Count);
        Assert.Equal("New", orgs[0].Name);
        Assert.Equal("Old", orgs[1].Name);
    }

    [Fact]
    public async Task SetStatusAsync_DeactivatesAndActivatesOrganization()
    {
        var dbName = Guid.NewGuid().ToString();
        var (ctx, svc) = CreateService(dbName);

        ctx.Organizations.Add(new Organization
        {
            Name = "Toggle Org",
            Slug = "toggleorg",
            Status = OrganizationStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var org = await ctx.Organizations.SingleAsync(o => o.Slug == "toggleorg");

        Assert.True(await svc.SetStatusAsync(org.Id, OrganizationStatus.Inactive));
        Assert.Equal(OrganizationStatus.Inactive,
            (await ctx.Organizations.AsNoTracking().SingleAsync(o => o.Id == org.Id)).Status);

        Assert.True(await svc.SetStatusAsync(org.Id, OrganizationStatus.Active));
        Assert.Equal(OrganizationStatus.Active,
            (await ctx.Organizations.AsNoTracking().SingleAsync(o => o.Id == org.Id)).Status);
    }

    [Fact]
    public async Task SetStatusAsync_UnknownId_ReturnsFalse()
    {
        var (_, svc) = CreateService();
        Assert.False(await svc.SetStatusAsync(int.MaxValue, OrganizationStatus.Inactive));
    }

    // -------------------------------------------------------------------------
    // Tenant-scoped: GetCurrentAsync / RenameCurrentAsync / GetCurrentMembersAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetCurrentAsync_ReturnsOrganizationForResolvedTenant()
    {
        var dbName = Guid.NewGuid().ToString();
        var (noCtx, _) = CreateService(dbName);

        var org = new Organization { Name = "Current Org", Slug = "currentorg", Status = OrganizationStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        noCtx.Organizations.Add(org);
        await noCtx.SaveChangesAsync();

        var (_, tenantSvc) = CreateService(dbName, org.Id);
        var current = await tenantSvc.GetCurrentAsync();

        Assert.NotNull(current);
        Assert.Equal("Current Org", current.Name);
    }

    [Fact]
    public async Task GetCurrentAsync_WithNoResolvedTenant_ReturnsNull()
    {
        var dbName = Guid.NewGuid().ToString();
        var (noCtx, noSvc) = CreateService(dbName);

        noCtx.Organizations.Add(new Organization { Name = "Some Org", Slug = "someorg", Status = OrganizationStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await noCtx.SaveChangesAsync();

        Assert.Null(await noSvc.GetCurrentAsync());
    }

    [Fact]
    public async Task RenameCurrentAsync_UpdatesNameForResolvedTenant()
    {
        var dbName = Guid.NewGuid().ToString();
        var (noCtx, _) = CreateService(dbName);

        var org = new Organization { Name = "Old Name", Slug = "renameorg", Status = OrganizationStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        noCtx.Organizations.Add(org);
        await noCtx.SaveChangesAsync();

        var (ctx, tenantSvc) = CreateService(dbName, org.Id);

        Assert.True(await tenantSvc.RenameCurrentAsync("New Name"));
        Assert.Equal("New Name",
            (await ctx.Organizations.AsNoTracking().SingleAsync(o => o.Id == org.Id)).Name);
    }

    [Fact]
    public async Task RenameCurrentAsync_CannotChangeName_WhenNoTenantResolved()
    {
        var (_, svc) = CreateService(); // no tenant
        Assert.False(await svc.RenameCurrentAsync("Hacked Name"));
    }

    [Fact]
    public async Task RenameCurrentAsync_EmptyName_ReturnsFalse()
    {
        var dbName = Guid.NewGuid().ToString();
        var (noCtx, _) = CreateService(dbName);

        var org = new Organization { Name = "Good Name", Slug = "goodorg", Status = OrganizationStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        noCtx.Organizations.Add(org);
        await noCtx.SaveChangesAsync();

        var (_, tenantSvc) = CreateService(dbName, org.Id);

        Assert.False(await tenantSvc.RenameCurrentAsync("   "));
    }

    [Fact]
    public async Task GetCurrentMembersAsync_ReturnsMembersForResolvedTenantOnly()
    {
        var dbName = Guid.NewGuid().ToString();
        var (noCtx, _) = CreateService(dbName);

        var orgA = new Organization { Name = "A", Slug = "orga3", Status = OrganizationStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var orgB = new Organization { Name = "B", Slug = "orgb3", Status = OrganizationStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        noCtx.Organizations.AddRange(orgA, orgB);
        await noCtx.SaveChangesAsync();

        noCtx.Users.AddRange(
            new IdentityUser { Id = "ua3", UserName = "ua3@x.com", Email = "ua3@x.com" },
            new IdentityUser { Id = "ub3", UserName = "ub3@x.com", Email = "ub3@x.com" });
        await noCtx.SaveChangesAsync();

        noCtx.OrganizationMembers.AddRange(
            new OrganizationMember { OrganizationId = orgA.Id, UserId = "ua3", Role = OrganizationRole.OrganizationOwner, CreatedAt = DateTime.UtcNow },
            new OrganizationMember { OrganizationId = orgB.Id, UserId = "ub3", Role = OrganizationRole.OrganizationOwner, CreatedAt = DateTime.UtcNow });
        await noCtx.SaveChangesAsync();

        var (_, tenantSvcA) = CreateService(dbName, orgA.Id);
        var members = await tenantSvcA.GetCurrentMembersAsync();

        Assert.Single(members);
        Assert.Equal("ua3", members[0].UserId);
    }

    // -------------------------------------------------------------------------
    // Hostname resolution after creation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AfterCreateAsync_NewOrg_ResolvesFromSubdomain()
    {
        var dbName = Guid.NewGuid().ToString();
        var (ctx, svc) = CreateService(dbName);

        var result = await svc.CreateAsync(
            "Resolve Test Org",
            "resolvetest",
            "Owner",
            "resolve@example.com");

        Assert.True(result.Success);

        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.Host = new Microsoft.AspNetCore.Http.HostString("resolvetest.punitbola.com");

        var resolver = new TenantResolver(
            new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = httpContext },
            ctx,
            new TenantHostParser(),
            Microsoft.Extensions.Options.Options.Create(new TenantOptions { BaseDomain = BaseDomain }));

        var resolvedId = await resolver.ResolveOrganizationIdAsync();

        Assert.Equal(result.Organization!.Id, resolvedId);
    }

    [Fact]
    public async Task AfterSetStatus_Inactive_OrgDoesNotResolveFromSubdomain()
    {
        var dbName = Guid.NewGuid().ToString();
        var (ctx, svc) = CreateService(dbName);

        var result = await svc.CreateAsync(
            "Inactive Test Org",
            "inactivetest",
            "Owner",
            "inactive@example.com");

        Assert.True(result.Success);

        await svc.SetStatusAsync(result.Organization!.Id, OrganizationStatus.Inactive);

        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.Host = new Microsoft.AspNetCore.Http.HostString("inactivetest.punitbola.com");

        var resolver = new TenantResolver(
            new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = httpContext },
            ctx,
            new TenantHostParser(),
            Microsoft.Extensions.Options.Options.Create(new TenantOptions { BaseDomain = BaseDomain }));

        Assert.Null(await resolver.ResolveOrganizationIdAsync());
    }

    // -------------------------------------------------------------------------
    // Logo & Subscription Provisioning
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateCurrentLogoAsync_UpdatesAndClearsLogo()
    {
        var dbName = Guid.NewGuid().ToString();
        var (_, adminSvc) = CreateService(dbName);
        var created = await adminSvc.CreateAsync("Logo Org", "logoorg", "Owner", "logo@example.com");
        Assert.True(created.Success);

        var (tenantCtx, tenantSvc) = CreateService(dbName, created.Organization!.Id);

        // 1. Update logo
        var updated = await tenantSvc.UpdateCurrentLogoAsync("organizations/1/logo/logo.png");
        Assert.True(updated);

        var org = await tenantSvc.GetCurrentAsync();
        Assert.NotNull(org);
        Assert.Equal("organizations/1/logo/logo.png", org.LogoPath);

        // 2. Clear logo
        var cleared = await tenantSvc.UpdateCurrentLogoAsync(null);
        Assert.True(cleared);

        org = await tenantSvc.GetCurrentAsync();
        Assert.NotNull(org);
        Assert.Null(org.LogoPath);
    }

    [Fact]
    public async Task UpdateCurrentHeroImageAsync_UpdatesAndClearsHeroImagePath()
    {
        var dbName = Guid.NewGuid().ToString();
        var (_, platformSvc) = CreateService(dbName);
        var created = await platformSvc.CreateAsync("Hero Tenant", "herotenant", "Owner", "hero@test.com");
        Assert.True(created.Success);

        var (_, tenantSvc) = CreateService(dbName, created.Organization!.Id);

        // 1. Update hero image as tenant
        var updated = await tenantSvc.UpdateCurrentHeroImageAsync("organizations/1/hero/hero.png");
        Assert.True(updated);

        var org = await tenantSvc.GetCurrentAsync();
        Assert.NotNull(org);
        Assert.Equal("organizations/1/hero/hero.png", org.HeroImagePath);

        // 2. Clear hero image as tenant
        var cleared = await tenantSvc.UpdateCurrentHeroImageAsync(null);
        Assert.True(cleared);

        org = await tenantSvc.GetCurrentAsync();
        Assert.NotNull(org);
        Assert.Null(org.HeroImagePath);
    }

    [Fact]
    public async Task UpdateHeroImageForOrgAsync_PlatformAdminUpdatesAnyOrg()
    {
        var dbName = Guid.NewGuid().ToString();
        var (_, platformSvc) = CreateService(dbName);
        var created = await platformSvc.CreateAsync("Platform Hero Org", "platformhero", "Owner", "platformhero@test.com");
        Assert.True(created.Success);

        var orgId = created.Organization!.Id;

        // Platform admin updates hero image by organization ID directly
        var updated = await platformSvc.UpdateHeroImageForOrgAsync(orgId, "organizations/2/hero/hero.jpg");
        Assert.True(updated);

        var summary = await platformSvc.GetByIdAsync(orgId);
        Assert.NotNull(summary);
        Assert.Equal("organizations/2/hero/hero.jpg", summary.HeroImagePath);

        // Platform admin clears hero image
        var cleared = await platformSvc.UpdateHeroImageForOrgAsync(orgId, null);
        Assert.True(cleared);

        summary = await platformSvc.GetByIdAsync(orgId);
        Assert.NotNull(summary);
        Assert.Null(summary.HeroImagePath);
    }

    [Fact]
    public async Task CreateAsync_AutomaticallyProvisionsActiveTrialSubscription()
    {
        var (context, svc) = CreateService();

        context.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Name = "Free Trial",
            Price = 0m,
            IsFree = true,
            IsActive = true
        });
        await context.SaveChangesAsync();

        var result = await svc.CreateAsync(
            "Trial Sub Org",
            "trialsuborg",
            "Owner User",
            "trial@example.com");

        Assert.True(result.Success);
        Assert.NotNull(result.Organization);

        var sub = await context.Subscriptions
            .FirstOrDefaultAsync(s => s.OrganizationId == result.Organization.Id);

        Assert.NotNull(sub);
        Assert.Equal(SubscriptionStatus.Trial, sub.Status);
        Assert.NotNull(sub.TrialEndDate);
        Assert.True(sub.TrialEndDate > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(25)));
    }

    // -------------------------------------------------------------------------
    // Minimal Identity UserStore backed by EF InMemory
    // -------------------------------------------------------------------------

    private sealed class InMemoryUserStore
        : IUserStore<IdentityUser>,
          IUserEmailStore<IdentityUser>,
          IUserPasswordStore<IdentityUser>
    {
        private readonly ApplicationDbContext _context;
        public InMemoryUserStore(ApplicationDbContext context) => _context = context;

        public async Task<IdentityResult> CreateAsync(IdentityUser user, CancellationToken _)
        {
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return IdentityResult.Success;
        }

        public async Task<IdentityUser?> FindByEmailAsync(string normalizedEmail, CancellationToken _)
            => await _context.Users.FirstOrDefaultAsync(u =>
                string.Equals(u.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase));

        public async Task<IdentityUser?> FindByIdAsync(string userId, CancellationToken _)
            => await _context.Users.FindAsync(userId);

        public Task<string> GetUserIdAsync(IdentityUser user, CancellationToken _) => Task.FromResult(user.Id);
        public Task<string?> GetUserNameAsync(IdentityUser user, CancellationToken _) => Task.FromResult(user.UserName);
        public Task SetUserNameAsync(IdentityUser user, string? n, CancellationToken _) { user.UserName = n; return Task.CompletedTask; }
        public Task<string?> GetNormalizedUserNameAsync(IdentityUser user, CancellationToken _) => Task.FromResult(user.NormalizedUserName);
        public Task SetNormalizedUserNameAsync(IdentityUser user, string? n, CancellationToken _) { user.NormalizedUserName = n; return Task.CompletedTask; }
        public Task<IdentityResult> UpdateAsync(IdentityUser user, CancellationToken _) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> DeleteAsync(IdentityUser user, CancellationToken _) => Task.FromResult(IdentityResult.Success);
        public async Task<IdentityUser?> FindByNameAsync(string normalizedUserName, CancellationToken _)
            => await _context.Users.FirstOrDefaultAsync(u => u.NormalizedUserName == normalizedUserName);

        // IUserEmailStore
        public Task SetEmailAsync(IdentityUser u, string? e, CancellationToken _) { u.Email = e; return Task.CompletedTask; }
        public Task<string?> GetEmailAsync(IdentityUser u, CancellationToken _) => Task.FromResult(u.Email);
        public Task<bool> GetEmailConfirmedAsync(IdentityUser u, CancellationToken _) => Task.FromResult(u.EmailConfirmed);
        public Task SetEmailConfirmedAsync(IdentityUser u, bool c, CancellationToken _) { u.EmailConfirmed = c; return Task.CompletedTask; }
        public Task SetNormalizedEmailAsync(IdentityUser u, string? ne, CancellationToken _) { u.NormalizedEmail = ne; return Task.CompletedTask; }
        public Task<string?> GetNormalizedEmailAsync(IdentityUser u, CancellationToken _) => Task.FromResult(u.NormalizedEmail);

        // IUserPasswordStore
        public Task SetPasswordHashAsync(IdentityUser u, string? h, CancellationToken _) { u.PasswordHash = h; return Task.CompletedTask; }
        public Task<string?> GetPasswordHashAsync(IdentityUser u, CancellationToken _) => Task.FromResult(u.PasswordHash);
        public Task<bool> HasPasswordAsync(IdentityUser u, CancellationToken _) => Task.FromResult(u.PasswordHash != null);

        public void Dispose() { }
    }

    /// <summary>
    /// Minimal IUserTwoFactorTokenProvider that returns a deterministic token.
    /// Used so GenerateEmailConfirmationTokenAsync works in unit tests without
    /// requiring ASP.NET Core DataProtection infrastructure.
    /// </summary>
    private sealed class StubTokenProvider : IUserTwoFactorTokenProvider<IdentityUser>
    {
        private const string FakeToken = "stub-activation-token";

        public Task<string> GenerateAsync(string purpose, UserManager<IdentityUser> manager, IdentityUser user)
            => Task.FromResult(FakeToken);

        public Task<bool> ValidateAsync(string purpose, string token, UserManager<IdentityUser> manager, IdentityUser user)
            => Task.FromResult(token == FakeToken);

        public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<IdentityUser> manager, IdentityUser user)
            => Task.FromResult(false);
    }
}
