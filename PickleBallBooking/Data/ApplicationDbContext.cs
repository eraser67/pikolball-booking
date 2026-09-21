using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Data;

/// <summary>
/// EF Core database context for the Pickleball Booking System.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<IdentityUser>
{
    /// <summary>
    /// The current request's tenant context (Phase 21). It drives the global query
    /// filters that isolate tenant-owned data. When it is <c>null</c> or unresolved,
    /// the filters match nothing, so tenant-owned data is never leaked.
    ///
    /// The interface (not the concrete type) is held so tests can supply a simple
    /// stub. A mutable <see cref="TenantContext"/> is resolved from DI at runtime,
    /// which lets the captured reference reflect the current request's organization.
    /// </summary>
    private readonly ITenantContext? _tenantContext;

        /// <summary>
    /// The current organization id used by the global query filters. <c>null</c>
    /// means "no tenant" and causes tenant-owned queries to return no rows.
    /// </summary>
    public int? CurrentOrganizationId => _tenantContext?.OrganizationId;

        /// <summary>
    /// Binds this context to an organization. Intended for tests and for startup
    /// seeding scopes, which construct the context before any HTTP request exists.
    /// Normal application code must never call this - the tenant is resolved from
    /// the authenticated user (or the anonymous Pikolball fallback) by middleware.
    /// </summary>
    public void SetTenantOrganizationId(int organizationId)
    {
        if (_tenantContext is TenantContext mutable)
        {
            mutable.OrganizationId = organizationId;
        }
    }


    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : this(options, tenantContext: null)
    {
    }

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ITenantContext? tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Court> Courts => Set<Court>();

    public DbSet<TimeSlot> TimeSlots => Set<TimeSlot>();

    public DbSet<Pricing> Pricings => Set<Pricing>();

    public DbSet<Booking> Bookings => Set<Booking>();

    public DbSet<BookingTimeSlot> BookingTimeSlots => Set<BookingTimeSlot>();

    public DbSet<CourtTimeSlot> CourtTimeSlots => Set<CourtTimeSlot>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();

    // Phase 25: payment tables
    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<OrganizationPaymentSettings> OrganizationPaymentSettings => Set<OrganizationPaymentSettings>();

    // Phase 26: subscription tables (global — no tenant query filter)
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();

    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    /// <summary>
    /// The set of tenant-owned entity CLR types. Used by the write guard below so a
    /// single implementation covers every tenant-owned entity.
    /// </summary>
    private static readonly HashSet<Type> TenantOwnedTypes = new()
    {
        typeof(Court),
        typeof(Booking),
        typeof(Pricing),
        typeof(CourtTimeSlot),
        typeof(BookingTimeSlot),
        typeof(Payment),
        typeof(OrganizationPaymentSettings),
    };

    public override int SaveChanges()
    {
        GuardTenantWrites();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        GuardTenantWrites();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// When true, tenant write guard checks are bypassed. Used for customer-facing
    /// operations (such as payment creation or submission via booking reference)
    /// that must write or update a tenant-owned record without an ambient tenant context.
    /// </summary>
    public bool SuppressTenantWriteGuard { get; set; }

    /// <summary>
    /// Phase 21 write protection.
    ///
    /// 1. Newly-added tenant-owned entities are automatically stamped with the
    ///    current organization id. Callers cannot choose the tenant: an
    ///    OrganizationId they set is overwritten with the resolved tenant.
    /// 2. Any tenant-owned entity being added/modified/deleted that does not belong
    ///    to the current organization is rejected (cross-tenant write protection).
    /// 3. When no tenant is resolved, tenant-owned writes are rejected outright.
    ///
    /// This runs on every save, so it also catches writes performed through any
    /// service, page handler, or seeder that goes through the context.
    /// </summary>
    private void GuardTenantWrites()
    {
        if (SuppressTenantWriteGuard)
        {
            return;
        }

        var current = CurrentOrganizationId;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (!TenantOwnedTypes.Contains(entry.Entity.GetType()))
            {
                continue;
            }

            var organizationEntry = entry.Property(nameof(Court.OrganizationId));

            // Added / Modified / Deleted of another tenant's row is never allowed.
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                if (entry.State == EntityState.Added)
                {
                    // Stamp with the resolved tenant; ignore any caller-supplied value.
                    if (current is null)
                    {
                        throw new InvalidOperationException(
                            "Cannot save tenant-owned data because no organization is resolved for the current request.");
                    }

                    organizationEntry.CurrentValue = current.Value;
                    continue;
                }

                // Modified / Deleted: the loaded entity must belong to the current tenant.
                var existingOrgId = (int)(organizationEntry.OriginalValue ?? 0);
                if (current is null || existingOrgId != current.Value)
                {
                    throw new InvalidOperationException(
                        "Cannot modify or delete data that belongs to another organization.");
                }
            }
        }
    }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Booking entity
        modelBuilder.Entity<Booking>(entity =>
        {
            entity.HasIndex(b => b.BookingReference).IsUnique();

            // Range-friendly query index for overlap checks and availability lookups.
            entity.HasIndex(b => new { b.CourtId, b.BookingDate, b.StartTime, b.EndTime });

            entity.HasOne(b => b.Court)
                .WithMany()
                .HasForeignKey(b => b.CourtId)
                .OnDelete(DeleteBehavior.Restrict);

            // Tenant relationship. Restrict prevents deleting an organization that
            // still owns bookings.
            entity.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(b => b.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure BookingTimeSlot relationship
            entity.HasMany(b => b.TimeSlots)
                .WithOne(bts => bts.Booking)
                .HasForeignKey(bts => bts.BookingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure BookingTimeSlot entity
        modelBuilder.Entity<BookingTimeSlot>(entity =>
        {
            entity.HasKey(bts => bts.Id);

            // Unique constraint: prevent duplicate (OrganizationId, CourtId, BookingDate, TimeSlotId)
            // for active bookings. This prevents double-booking at the database level with
            // transactional safety, scoped per organization.
            entity.HasIndex(bts => new { bts.OrganizationId, bts.CourtId, bts.BookingDate, bts.TimeSlotId })
                .IsUnique()
                .HasFilter("\"IsActive\" = true")
                .HasDatabaseName("IX_BookingTimeSlot_OrganizationId_CourtId_BookingDate_TimeSlotId_Active");

            // Index on BookingId for fast lookups of all slots in a booking
            entity.HasIndex(bts => bts.BookingId)
                .HasDatabaseName("IX_BookingTimeSlot_BookingId");

            // Index on TimeSlotId for availability queries
            entity.HasIndex(bts => bts.TimeSlotId)
                .HasDatabaseName("IX_BookingTimeSlot_TimeSlotId");

            // Index for availability queries: find booked slots for a court on a date
            entity.HasIndex(bts => new { bts.CourtId, bts.BookingDate, bts.IsActive })
                .HasDatabaseName("IX_BookingTimeSlot_CourtId_BookingDate_IsActive");

            // FK to Booking (already configured on Booking side)
            entity.HasOne(bts => bts.Booking)
                .WithMany(b => b.TimeSlots)
                .HasForeignKey(bts => bts.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            // FK to TimeSlot
            entity.HasOne(bts => bts.TimeSlot)
                .WithMany()
                .HasForeignKey(bts => bts.TimeSlotId)
                .OnDelete(DeleteBehavior.Restrict);

            // Tenant relationship. Restrict prevents deleting an organization that
            // still owns booking time slots.
            entity.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(bts => bts.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Configure CourtTimeSlot entity
        modelBuilder.Entity<CourtTimeSlot>(entity =>
        {
            entity.HasKey(cts => cts.Id);

            // Unique constraint: one court can have each time slot status defined only once,
            // scoped per organization so separate organizations can have their own
            // Court/TimeSlot combinations.
            entity.HasIndex(cts => new { cts.OrganizationId, cts.CourtId, cts.TimeSlotId })
                .IsUnique()
                .HasDatabaseName("IX_CourtTimeSlot_OrganizationId_CourtId_TimeSlotId");

            // FK to Court
            entity.HasOne(cts => cts.Court)
                .WithMany()
                .HasForeignKey(cts => cts.CourtId)
                .OnDelete(DeleteBehavior.Cascade);

            // FK to TimeSlot
            entity.HasOne(cts => cts.TimeSlot)
                .WithMany()
                .HasForeignKey(cts => cts.TimeSlotId)
                .OnDelete(DeleteBehavior.Cascade);

            // Tenant relationship. Restrict prevents deleting an organization that
            // still owns court time slot configuration.
            entity.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(cts => cts.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Configure Court entity
        modelBuilder.Entity<Court>(entity =>
        {
            // Tenant relationship. Restrict prevents deleting an organization that
            // still owns courts.
            entity.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(c => c.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Configure Pricing entity
        modelBuilder.Entity<Pricing>(entity =>
        {
            // Tenant relationship. Restrict prevents deleting an organization that
            // still owns pricing rules.
            entity.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(p => p.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Configure Organization entity
        modelBuilder.Entity<Organization>(entity =>
        {
            entity.HasKey(o => o.Id);

            entity.Property(o => o.Name)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(o => o.Slug)
                .IsRequired()
                .HasMaxLength(100);

            // Slug is the future subdomain key and must be unique.
            entity.HasIndex(o => o.Slug).IsUnique();

            entity.Property(o => o.Status).IsRequired();

            entity.Property(o => o.CreatedAt).IsRequired();

            entity.Property(o => o.UpdatedAt).IsRequired();
        });

        // Configure OrganizationMember entity
        modelBuilder.Entity<OrganizationMember>(entity =>
        {
            entity.HasKey(m => m.Id);

            entity.Property(m => m.UserId).IsRequired();

            entity.Property(m => m.Role).IsRequired();

            entity.Property(m => m.CreatedAt).IsRequired();

            // A user can belong to a given organization only once.
            entity.HasIndex(m => new { m.OrganizationId, m.UserId })
                .IsUnique()
                .HasDatabaseName("IX_OrganizationMember_OrganizationId_UserId");

            // Index for "which organizations does this user belong to" lookups.
            entity.HasIndex(m => m.UserId)
                .HasDatabaseName("IX_OrganizationMember_UserId");

            // Index for filtering members by organization and role.
            entity.HasIndex(m => new { m.OrganizationId, m.Role })
                .HasDatabaseName("IX_OrganizationMember_OrganizationId_Role");

            // FK to Organization. Restrict: deleting an organization must not silently
            // cascade-delete its membership records.
            entity.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(m => m.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            // FK to AspNetUsers. Restrict: membership rows must not be cascade-deleted
            // when an Identity user is deleted. This matches the existing Identity
            // design, where Identity's own claim/login/role tables cascade but domain
            // data does not.
            entity.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

                // NOTE: The PostgreSQL tenant-aware booking exclusion constraint
        // (EX_Bookings_NoOverlap on (OrganizationId WITH =, CourtId WITH =,
        // BookingPeriod WITH &&) WHERE "BookingStatus" <> 2) CANNOT be represented
        // by EF Core's model builder, so it is intentionally NOT configured here.
        // It must be created/dropped via raw migration SQL in Phase 20.4. The existing
        // BookingPeriod (stored tsrange) column and its PostgreSQL configuration are
        // preserved unchanged; this step neither removes nor weakens that constraint.

        // -------------------------------------------------------------------
        // Phase 21: tenant isolation via EF Core global query filters.
        //
        // Every tenant-owned entity is automatically scoped to the current
        // organization. When the tenant is unresolved (null), the predicate is
        // false and the entity is invisible - a safe-by-default posture.
        //
        // TimeSlot is global/shared and intentionally has NO filter.
        // Organization and OrganizationMember intentionally have NO filter: they
        // are governed by their own authorization/context rules (a user must be
        // able to read their own membership before a tenant can be resolved).
        // -------------------------------------------------------------------
        modelBuilder.Entity<Court>()
            .HasQueryFilter(c => CurrentOrganizationId != null && c.OrganizationId == CurrentOrganizationId);

        modelBuilder.Entity<Booking>()
            .HasQueryFilter(b => CurrentOrganizationId != null && b.OrganizationId == CurrentOrganizationId);

        modelBuilder.Entity<Pricing>()
            .HasQueryFilter(p => CurrentOrganizationId != null && p.OrganizationId == CurrentOrganizationId);

        modelBuilder.Entity<CourtTimeSlot>()
            .HasQueryFilter(cts => CurrentOrganizationId != null && cts.OrganizationId == CurrentOrganizationId);

        modelBuilder.Entity<BookingTimeSlot>()
            .HasQueryFilter(bts => CurrentOrganizationId != null && bts.OrganizationId == CurrentOrganizationId);

        // Phase 25: payment entity filters.
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasQueryFilter(p => CurrentOrganizationId != null && p.OrganizationId == CurrentOrganizationId);

            entity.HasOne(p => p.Booking)
                .WithMany()
                .HasForeignKey(p => p.BookingId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(p => p.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(p => p.BookingId)
                .HasDatabaseName("IX_Payment_BookingId");

            entity.HasIndex(p => new { p.OrganizationId, p.PaymentStatus })
                .HasDatabaseName("IX_Payment_OrganizationId_Status");
        });

        modelBuilder.Entity<OrganizationPaymentSettings>(entity =>
        {
            entity.HasQueryFilter(s => CurrentOrganizationId != null && s.OrganizationId == CurrentOrganizationId);

            // One active settings record per organization.
            entity.HasIndex(s => s.OrganizationId)
                .IsUnique()
                .HasDatabaseName("IX_OrganizationPaymentSettings_OrganizationId");

            entity.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(s => s.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Phase 26: subscription plan + subscription configuration.
        // SubscriptionPlans are global (no tenant filter) — platform-managed.
        // Subscriptions are also global: platform admins must read across tenants.
        modelBuilder.Entity<SubscriptionPlan>(entity =>
        {
            entity.HasKey(p => p.Id);

            entity.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(p => p.Price)
                .HasColumnType("numeric(10,2)");

            entity.HasIndex(p => p.Name)
                .IsUnique()
                .HasDatabaseName("IX_SubscriptionPlan_Name");
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.HasKey(s => s.Id);

            // One subscription per organization (unique constraint).
            // To change plan, update the existing row rather than creating new ones.
            entity.HasIndex(s => s.OrganizationId)
                .IsUnique()
                .HasDatabaseName("IX_Subscription_OrganizationId");

            entity.HasOne(s => s.Organization)
                .WithMany()
                .HasForeignKey(s => s.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(s => s.Plan)
                .WithMany(sp => sp.Subscriptions)
                .HasForeignKey(s => s.PlanId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
