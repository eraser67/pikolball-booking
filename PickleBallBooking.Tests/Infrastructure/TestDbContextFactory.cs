using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory;
using PickleBallBooking.Data;
using PickleBallBooking.Services;

namespace PickleBallBooking.Tests.Infrastructure;

/// <summary>
/// Phase 21: helpers for building <see cref="ApplicationDbContext"/> instances bound
/// to a specific organization, because the global query filters and the write guard
/// require a resolved tenant.
///
/// Some tests intentionally build a context with NO tenant (to prove that an
/// unresolved tenant sees/writes nothing) - use <see cref="CreateWithoutTenant"/>.
/// </summary>
internal static class TestDbContextFactory
{
    /// <summary>
    /// Creates an in-memory context bound to <paramref name="organizationId"/>. All
    /// tenant-owned queries are scoped to that organization and all tenant-owned
    /// writes are stamped with it.
    /// </summary>
    public static ApplicationDbContext CreateInMemory(string databaseName, int organizationId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            // OrganizationService.CreateAsync uses a transaction for atomicity; the InMemory
            // provider silently ignores transactions, which is fine for unit tests. Suppress
            // the warning so it does not throw.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options, new TenantContext { OrganizationId = organizationId });
    }

    /// <summary>
    /// Creates an in-memory context with NO resolved tenant. Tenant-owned queries
    /// return nothing and tenant-owned writes are rejected.
    /// </summary>
    public static ApplicationDbContext CreateWithoutTenant(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options, new TenantContext { OrganizationId = null });
    }

    /// <summary>
    /// A tiny mutable tenant context for tests that need to switch the current
    /// organization on a live context (to simulate a request changing tenants).
    /// </summary>
    public static TenantContext Tenant(int? organizationId) => new() { OrganizationId = organizationId };
}
