using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Services;

var builder = WebApplication.CreateBuilder(args);

// HSTS configuration with preload and subdomains support
builder.Services.AddHsts(options =>
{
    options.Preload = true;
    options.IncludeSubDomains = true;
    options.MaxAge = TimeSpan.FromDays(365);
});

// Rate limiting on sensitive endpoints (login, payments)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth-limit", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    options.AddPolicy("payment-limit", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

// Standardize application culture to Philippine Peso (₱)
var defaultCulture = new CultureInfo("en-PH");
defaultCulture.NumberFormat.CurrencySymbol = "₱";
CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
CultureInfo.DefaultThreadCurrentUICulture = defaultCulture;

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture(defaultCulture);
    options.SupportedCultures = new List<CultureInfo> { defaultCulture };
    options.SupportedUICultures = new List<CultureInfo> { defaultCulture };
});

// Add services to the container.
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin");
});

// Phase 23: platform-level authorization policy. Only Identity users in the
// PlatformAdmin role can manage organizations. This reuses the existing ASP.NET Core
// Identity role infrastructure (the same one AdminSeeder uses) and does NOT create a
// second organization role system.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(PlatformRoles.PlatformAdminPolicy, policy =>
        policy.RequireAuthenticatedUser().RequireRole(PlatformRoles.PlatformAdmin));

    // Phase 23: tenant administration requires an authenticated member of the
    // hostname-resolved organization.
    options.AddTenantAdminPolicy();
});

// Phase 23: handler backing the tenant-admin policy.
builder.Services.AddTenantAdminAuthorization();

builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.Password.RequireNonAlphanumeric = false;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/Login";
    options.Cookie.HttpOnly = true;
    // Development: SameAsRequest so HTTP works on *.localhost subdomains.
    // Production: Always to enforce HTTPS.
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;

    var baseDomain = builder.Configuration["Tenant:BaseDomain"];
    if (!builder.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(baseDomain) && !baseDomain.Contains("localhost"))
    {
        options.Cookie.Domain = $".{baseDomain.TrimStart('.')}";
    }
});

builder.Services.AddScoped<ICourtService, CourtService>();
builder.Services.AddScoped<ITimeSlotService, TimeSlotService>();
builder.Services.AddScoped<IPricingService, PricingService>();
builder.Services.AddScoped<IBookingService, BookingService>();

// Phase 24: Supabase Storage for court images.
// Options are bound from "Supabase:Storage" (appsettings) with Supabase:ServiceRoleKey from user secrets.
builder.Services.Configure<SupabaseStorageOptions>(builder.Configuration.GetSection(SupabaseStorageOptions.SectionName));
builder.Services.AddHttpClient<ICourtImageStorage, SupabaseCourtImageStorage>();

// Phase 25: manual GCash payment workflow.
// PaymentProofOptions is SEPARATE from SupabaseStorageOptions:
//   - Court images: 3 MB (SupabaseStorageOptions.MaxCourtImageSizeBytes)
//   - Payment proofs: 1 MB (PaymentProofOptions.MaxFileSizeBytes)
builder.Services.Configure<PaymentProofOptions>(builder.Configuration.GetSection(PaymentProofOptions.SectionName));
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddHttpClient<IPaymentProofStorage, SupabasePaymentProofStorage>();

// Resend transactional email:
// IEmailService → ResendEmailService (typed HttpClient; IHttpClientFactory manages socket lifetime).
// BookingEmailService is scoped: it composes and fires emails for each request.
// Set Email:Enabled = true and set Email:ApiKey (via User Secrets or env var) to activate.
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddHttpClient<IEmailService, ResendEmailService>();
builder.Services.AddScoped<BookingEmailService>();

// Phase 23: organization/tenant administration.
builder.Services.AddSingleton<IReservedSlugs, ReservedSlugs>();
builder.Services.AddScoped<IOrganizationService, OrganizationService>();

// Phase 26: subscription management.
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();

// Phase 21/22: server-side tenant context and resolution.
// The context is request-scoped so concurrent requests never share an organization.
// ITenantResolver identifies the tenant from the request HOSTNAME (a strict
// single-label subdomain of Tenant:BaseDomain -> Organization.Slug) and, for
// authenticated users, verifies membership in that organization. The DbContext is
// constructed with the scoped tenant context so its global query filters and write
// guard always see the current request's organization.
builder.Services.Configure<TenantOptions>(builder.Configuration.GetSection(TenantOptions.SectionName));
builder.Services.AddSingleton<ITenantHostParser, TenantHostParser>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
builder.Services.AddScoped<ITenantResolver, TenantResolver>();

// Register the tenant-aware DbContext explicitly. A manual factory (rather than
// AddDbContext) is required so the scoped ITenantContext can be injected into the
// context constructor; AddDbContext only injects DbContextOptions by default.
builder.Services.AddScoped<ApplicationDbContext>(sp =>
{
    var tenantContext = sp.GetRequiredService<ITenantContext>();
    var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
    optionsBuilder.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null));
    return new ApplicationDbContext(optionsBuilder.Options, tenantContext);
});

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    KnownProxies = { System.Net.IPAddress.Loopback, System.Net.IPAddress.IPv6Loopback }
});

var localizationOptions = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(defaultCulture),
    SupportedCultures = new List<CultureInfo> { defaultCulture },
    SupportedUICultures = new List<CultureInfo> { defaultCulture }
};
app.UseRequestLocalization(localizationOptions);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // Show the full exception details (yellow screen) in development so errors
    // are visible instead of silently routing to the generic /Error page.
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    // Explicit HSTS configured in services
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Error/{0}");

// Skip HTTPS redirect in development: the ASP.NET dev cert only covers 'localhost',
// not '*.localhost' subdomains, so redirecting would break tenant subdomain testing.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();

// Phase 21/22: resolve the current request's organization AFTER authentication
// (so the user's identity is available for membership checks) but BEFORE
// authorization (so the TenantAdmin policy can see IsResolved = true).
//
// The tenant is selected from the request HOSTNAME only (a strict single-label
// subdomain of Tenant:BaseDomain -> Organization.Slug). It never trusts arbitrary
// Host/Forwarded-Host values: `ForwardedHeaders` is not configured, so the Host
// header is the sole hostname source, and the resolver rejects any host that is not
// a strict subdomain of the configured base domain or that maps to an unknown or
// inactive organization. Client-declared values (query/route/form/cookie/body and
// any OrganizationId) are never consulted.
app.UseMiddleware<TenantResolutionMiddleware>();

// Phase 26: subscription wall — redirects blocked org admins to /Admin/SubscriptionRequired.
// Must run after tenant resolution (needs OrganizationId) and after authentication (needs User.Identity).
app.UseMiddleware<SubscriptionWallMiddleware>();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

// Development-only: Database reset endpoint
if (app.Environment.IsDevelopment())
{
    app.MapPost("/dev/reset-database", async (WebApplication app) =>
    {
        try
        {
            await DatabaseResetSeeder.ResetAndSeedAsync(app);
            return Results.Ok(new { message = "Database reset and reseeded successfully!" });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    });
}

await AdminSeeder.SeedAsync(app);
await DemoDataSeeder.SeedAsync(app);
await SubscriptionSeeder.SeedAsync(app); // Phase 26: seed default plans and assign orgs.

app.Run();


