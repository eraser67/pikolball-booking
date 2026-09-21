using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Tests.Infrastructure;

internal static class PostgresTestDatabase
{
    private static string? _connectionString;

    /// <summary>
    /// Slug of the organization every PostgreSQL integration test attaches its
    /// tenant-owned rows to. Reuses the real "pikolball" organization seeded by the
    /// Phase 20.4 migration so tests exercise the same tenant the app uses, without
    /// inventing a second organization that would need its own cleanup.
    /// </summary>
    public const string TestOrganizationSlug = OrganizationDefaults.PikolballSlug;

    public static string ConnectionString => _connectionString ??= LoadConnectionString();

        /// <summary>
    /// Creates a context bound to a mutable tenant context that starts UNRESOLVED.
    /// Tests that first need to resolve an organization (via <see cref="GetOrCreateTestOrganizationIdAsync"/>)
    /// can create the context, resolve the id, then call <see cref="UseTenant"/> to bind it.
    /// </summary>
    public static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new ApplicationDbContext(options, new TenantContext { OrganizationId = null });
    }

    /// <summary>
    /// Binds a context created by <see cref="CreateContext()"/> to an organization so
    /// tenant-owned queries and writes are scoped/stamped accordingly.
    /// </summary>
    public static ApplicationDbContext UseTenant(this ApplicationDbContext context, int organizationId)
    {
        context.SetTenantOrganizationId(organizationId);
        return context;
    }

    /// <summary>
    /// Creates a context bound to <paramref name="organizationId"/>. All tenant-owned
    /// queries are scoped to that organization and tenant-owned writes are stamped
    /// with it (Phase 21). This is what normal application code always has.
    /// </summary>
    public static ApplicationDbContext CreateContext(int organizationId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new ApplicationDbContext(options, new TenantContext { OrganizationId = organizationId });
    }

    /// <summary>
    /// Resolves (creating if necessary) the organization used by integration-test
    /// fixtures. Resolution is by unique Slug so it never depends on a hard-coded id,
    /// and it is idempotent across runs.
    /// </summary>
    public static async Task<int> GetOrCreateTestOrganizationIdAsync(ApplicationDbContext context)
    {
        var existing = await context.Organizations
            .Where(o => o.Slug == TestOrganizationSlug)
            .Select(o => o.Id)
            .FirstOrDefaultAsync();

        if (existing != 0)
        {
            return existing;
        }

        var organization = new Organization
        {
            Name = "Pikolball",
            Slug = TestOrganizationSlug,
            Status = OrganizationStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        return organization.Id;
    }

    private static string LoadConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(ApplicationDbContext).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A PostgreSQL connection string named 'DefaultConnection' is required for integration tests.");
        }

        return connectionString;
    }
}
