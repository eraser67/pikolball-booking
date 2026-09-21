using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;

namespace PickleBallBooking.Tests;

/// <summary>
/// Phase 20.5: focused multi-tenant foundation tests.
///
/// These run against a real PostgreSQL instance because they assert schema-level
/// guarantees introduced by the Phase 20.4 migration (unique constraints, required
/// NOT NULL foreign keys) that the EF Core InMemory provider does not enforce.
///
/// Scope: this phase verifies the tenant foundation only. It deliberately does NOT
/// test tenant filtering/resolution, global query filters, or current-organization
/// behaviour - none of which are implemented yet.
///
/// Every test opens its own transaction and rolls back, so the shared database is
/// never mutated and the seeded Pikolball data is left exactly as it was.
/// </summary>
public class MultiTenantFoundationPostgresTests
{
    // -------------------------------------------------------------------------
    // Organization
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Organization_CanBePersisted_WithStatusAndTimestamps()
    {
        await using var context = PostgresTestDatabase.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var createdAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var updatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        var organization = new Organization
        {
            Name = "Foundation Persisted Org",
            Slug = $"foundation-persist-{Guid.NewGuid():N}",
            Status = OrganizationStatus.Inactive,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };

        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        var reloaded = await context.Organizations
            .SingleAsync(o => o.Id == organization.Id);

        Assert.True(reloaded.Id > 0);
        Assert.Equal("Foundation Persisted Org", reloaded.Name);
        Assert.Equal(OrganizationStatus.Inactive, reloaded.Status);
        Assert.Equal(createdAt, reloaded.CreatedAt);
        Assert.Equal(updatedAt, reloaded.UpdatedAt);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Organization_Slug_IsUnique()
    {
        await using var context = PostgresTestDatabase.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var slug = $"foundation-unique-{Guid.NewGuid():N}";

        context.Organizations.Add(new Organization
        {
            Name = "First",
            Slug = slug,
            Status = OrganizationStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        // A second organization with the same slug must be rejected by the unique
        // index on Organizations.Slug.
        context.Organizations.Add(new Organization
        {
            Name = "Second",
            Slug = slug,
            Status = OrganizationStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        await transaction.RollbackAsync();
    }

    // -------------------------------------------------------------------------
    // OrganizationMember
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OrganizationMember_User_CanBelongToOrganization_WithRelationships()
    {
        await using var context = PostgresTestDatabase.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var organization = new Organization
        {
            Name = "Membership Org",
            Slug = $"foundation-member-{Guid.NewGuid():N}",
            Status = OrganizationStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Organizations.Add(organization);

        var user = new IdentityUser
        {
            UserName = $"member-{Guid.NewGuid():N}@example.com",
            Email = $"member-{Guid.NewGuid():N}@example.com",
            EmailConfirmed = true
        };
        context.Users.Add(user);

        await context.SaveChangesAsync();

        var member = new OrganizationMember
        {
            OrganizationId = organization.Id,
            UserId = user.Id,
            Role = OrganizationRole.OrganizationOwner,
            CreatedAt = DateTime.UtcNow
        };
        context.OrganizationMembers.Add(member);
        await context.SaveChangesAsync();

        var reloaded = await context.OrganizationMembers.SingleAsync(m => m.Id == member.Id);

        Assert.Equal(organization.Id, reloaded.OrganizationId);
        Assert.Equal(user.Id, reloaded.UserId);
        Assert.Equal(OrganizationRole.OrganizationOwner, reloaded.Role);

        // Relationship back to the organization row.
        var owningOrganization = await context.Organizations.SingleAsync(o => o.Id == reloaded.OrganizationId);
        Assert.Equal(organization.Slug, owningOrganization.Slug);

        // Relationship back to the IdentityUser row.
        var owningUser = await context.Users.SingleAsync(u => u.Id == reloaded.UserId);
        Assert.Equal(user.UserName, owningUser.UserName);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task OrganizationMember_SameUserCannotJoinSameOrganizationTwice()
    {
        await using var context = PostgresTestDatabase.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var organization = new Organization
        {
            Name = "Membership Unique Org",
            Slug = $"foundation-member-unique-{Guid.NewGuid():N}",
            Status = OrganizationStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Organizations.Add(organization);

        var user = new IdentityUser
        {
            UserName = $"dup-{Guid.NewGuid():N}@example.com",
            Email = $"dup-{Guid.NewGuid():N}@example.com",
            EmailConfirmed = true
        };
        context.Users.Add(user);

        await context.SaveChangesAsync();

        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organization.Id,
            UserId = user.Id,
            Role = OrganizationRole.OrganizationStaff,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        // The unique index IX_OrganizationMember_OrganizationId_UserId must reject
        // a second membership for the same (organization, user) pair.
        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organization.Id,
            UserId = user.Id,
            Role = OrganizationRole.OrganizationAdmin,
            CreatedAt = DateTime.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        await transaction.RollbackAsync();
    }

    // -------------------------------------------------------------------------
    // Tenant-owned entities require an organization (NOT NULL FK)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Court_OrganizationId_IsRequired()
    {
        await using var context = PostgresTestDatabase.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        await AssertTenantForeignKeyViolationAsync(
            context,
            @"INSERT INTO ""Courts"" (""OrganizationId"", ""Name"", ""Status"", ""CreatedAt"", ""UpdatedAt"")
              VALUES (@org, 'Orphan Court', 0, now(), now())");

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Pricing_OrganizationId_IsRequired()
    {
        await using var context = PostgresTestDatabase.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        await AssertTenantForeignKeyViolationAsync(
            context,
            @"INSERT INTO ""Pricings"" (""OrganizationId"", ""DayType"", ""StartTime"", ""EndTime"", ""Price"", ""Status"", ""CreatedAt"", ""UpdatedAt"")
              VALUES (@org, 0, time '00:00', time '01:00', 100, 0, now(), now())");

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Booking_OrganizationId_IsRequired()
    {
        await using var context = PostgresTestDatabase.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        await AssertTenantForeignKeyViolationAsync(
            context,
            @"INSERT INTO ""Bookings"" (""OrganizationId"", ""BookingReference"", ""CustomerName"", ""CustomerPhone"", ""CustomerEmail"", ""CourtId"", ""BookingDate"", ""StartTime"", ""EndTime"", ""DurationHours"", ""Price"", ""BookingStatus"", ""CreatedAt"", ""UpdatedAt"")
              VALUES (@org, 'PB-FK-RAW', 'FK Test', '0900', 'fk@example.com', @org, CURRENT_DATE, time '09:00', time '10:00', 1, 100, 0, now(), now())");

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task CourtTimeSlot_OrganizationId_IsRequired()
    {
        await using var context = PostgresTestDatabase.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        await AssertTenantForeignKeyViolationAsync(
            context,
            @"INSERT INTO ""CourtTimeSlots"" (""OrganizationId"", ""CourtId"", ""TimeSlotId"", ""AvailabilityStatus"", ""CreatedAt"", ""UpdatedAt"")
              VALUES (@org, @org, @org, 0, now(), now())");

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task BookingTimeSlot_OrganizationId_IsRequired()
    {
        await using var context = PostgresTestDatabase.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        await AssertTenantForeignKeyViolationAsync(
            context,
            @"INSERT INTO ""BookingTimeSlots"" (""OrganizationId"", ""BookingId"", ""CourtId"", ""BookingDate"", ""TimeSlotId"", ""SlotOrder"", ""IsActive"", ""CreatedAt"", ""UpdatedAt"")
              VALUES (@org, @org, @org, CURRENT_DATE, @org, 0, true, now(), now())");

        await transaction.RollbackAsync();
    }

    // -------------------------------------------------------------------------
    // Global TimeSlot does NOT require an organization
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TimeSlot_DoesNotRequireOrganizationId()
    {
        await using var context = PostgresTestDatabase.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        // TimeSlot is global/shared: it has no OrganizationId column and must persist
        // without any organization reference.
        var timeSlot = new TimeSlot
        {
            StartTime = new TimeSpan(3, 0, 0),
            EndTime = new TimeSpan(4, 0, 0),
            Status = TimeSlotStatus.Active
        };

        context.TimeSlots.Add(timeSlot);
        await context.SaveChangesAsync();

        var reloaded = await context.TimeSlots.SingleAsync(ts => ts.Id == timeSlot.Id);
        Assert.Equal(new TimeSpan(3, 0, 0), reloaded.StartTime);
        Assert.Equal(new TimeSpan(4, 0, 0), reloaded.EndTime);

        await transaction.RollbackAsync();
    }

    /// <summary>
    /// Executes a raw INSERT whose OrganizationId points at a non-existent organization
    /// and asserts PostgreSQL rejects it with a foreign-key violation (SQLSTATE 23503).
    ///
    /// Raw SQL is used deliberately: the Phase 21 DbContext write guard stamps a valid
    /// tenant id onto tenant-owned entities added through EF, which would mask the
    /// database-level constraint. Going through Npgsql directly proves the FK/NOT NULL
    /// guarantees introduced in Phase 20.4 are still enforced.
    /// </summary>
    private static async Task AssertTenantForeignKeyViolationAsync(
        ApplicationDbContext context,
        string sql)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync(sql, new NpgsqlParameter("org", int.MaxValue)));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }
}
