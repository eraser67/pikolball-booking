using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;

namespace PickleBallBooking.Tests;

/// <summary>
/// Phase 20.5: multi-tenant foundation tests for the single-tenant default
/// organization resolution used by seeders, services, and fixtures.
///
/// Phase 21 update: tenant-owned writes are now stamped by the DbContext write guard
/// from the RESOLVED tenant (no longer by a hard-coded OrganizationDefaults call).
/// These tests therefore bind the context to an organization and assert the created
/// rows carry that organization's id. Tenant filtering/resolution itself is covered
/// by the dedicated Phase 21 tests.
/// </summary>
public class OrganizationDefaultsTests
{
    // The organization id the tenant-bound contexts below resolve to. It matches the
    // Pikolball organization row created by the first test's fixture.
    private const int PikolballOrganizationId = 42;

    private static ApplicationDbContext CreateContext()
    {
        return TestDbContextFactory.CreateInMemory(Guid.NewGuid().ToString(), PikolballOrganizationId);
    }

    [Fact]
    public async Task GetPikolballOrganizationIdAsync_CreatesOrganization_OnFirstUse()
    {
        await using var context = CreateContext();

        var id = await OrganizationDefaults.GetPikolballOrganizationIdAsync(context);

        var organization = await context.Organizations.SingleAsync();
        Assert.Equal(id, organization.Id);
        Assert.Equal("pikolball", organization.Slug);
        Assert.Equal("Pikolball", organization.Name);
        Assert.Equal(OrganizationStatus.Active, organization.Status);
    }

    [Fact]
    public async Task GetPikolballOrganizationIdAsync_IsIdempotent()
    {
        await using var context = CreateContext();

        var first = await OrganizationDefaults.GetPikolballOrganizationIdAsync(context);
        var second = await OrganizationDefaults.GetPikolballOrganizationIdAsync(context);

        Assert.Equal(first, second);
        Assert.Single(context.Organizations);
    }

    [Fact]
    public async Task GetPikolballOrganizationIdAsync_ReusesExistingOrganizationBySlug()
    {
        await using var context = CreateContext();

        // Simulates the Phase 20.4 migration having already created the organization
        // with a different Id, to prove resolution is by slug, never by a guessed Id.
        context.Organizations.Add(new Organization
        {
            Id = 42,
            Name = "Pikolball",
            Slug = "pikolball",
            Status = OrganizationStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var resolved = await OrganizationDefaults.GetPikolballOrganizationIdAsync(context);

        Assert.Equal(42, resolved);
        Assert.Single(context.Organizations);
    }

    [Fact]
    public async Task CourtService_CreateAsync_AttachesResolvedOrganization()
    {
        await using var context = CreateContext();
        await SeedOrganizationAsync(context);
        var service = new CourtService(context);

        var court = await service.CreateAsync("Court A", "Test court");

        Assert.Equal(PikolballOrganizationId, court.OrganizationId);
    }

    [Fact]
    public async Task PricingService_CreateAsync_AttachesResolvedOrganization()
    {
        await using var context = CreateContext();
        await SeedOrganizationAsync(context);
        var service = new PricingService(context);

        var pricing = await service.CreateAsync(DayType.Weekday, new TimeSpan(9, 0, 0), new TimeSpan(10, 0, 0), 200m);

        Assert.Equal(PikolballOrganizationId, pricing.OrganizationId);
    }

    /// <summary>
    /// Seeds the Pikolball organization row with the id the tenant-bound contexts
    /// resolve to, so tenant-owned writes have a valid FK target.
    /// </summary>
    private static async Task SeedOrganizationAsync(ApplicationDbContext context)
    {
        context.Organizations.Add(new Organization
        {
            Id = PikolballOrganizationId,
            Name = "Pikolball",
            Slug = "pikolball",
            Status = OrganizationStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }
}
