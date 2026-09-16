using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using PickleBallBooking.Models;

namespace PickleBallBooking.Data;

/// <summary>
/// EF Core database context for the Pickleball Booking System.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<IdentityUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Court> Courts => Set<Court>();

    public DbSet<TimeSlot> TimeSlots => Set<TimeSlot>();

    public DbSet<Pricing> Pricings => Set<Pricing>();

    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Booking>(entity =>
        {
            entity.HasIndex(b => b.BookingReference).IsUnique();

            // Legacy discrete-slot protection retained until booking logic is migrated.
            entity.HasIndex(b => new { b.CourtId, b.BookingDate, b.TimeSlotId })
                .IsUnique()
                .HasFilter("\"BookingStatus\" <> 2"); // 2 = Cancelled

            // Range-friendly query index for future overlap checks and availability lookups.
            entity.HasIndex(b => new { b.CourtId, b.BookingDate, b.StartTime, b.EndTime });

            entity.Property<NpgsqlRange<DateTime>>("BookingPeriod")
                .HasColumnType("tsrange")
                .HasComputedColumnSql("tsrange((\"BookingDate\"::timestamp + \"StartTime\"), (\"BookingDate\"::timestamp + \"EndTime\"), '[)')", stored: true);

            entity.HasOne(b => b.Court)
                .WithMany()
                .HasForeignKey(b => b.CourtId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(b => b.TimeSlot)
                .WithMany()
                .HasForeignKey(b => b.TimeSlotId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
