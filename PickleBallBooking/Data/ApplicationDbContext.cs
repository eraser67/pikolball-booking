using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
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

            // Prevent double booking at the database level: only one non-cancelled
            // booking may exist for a given Court + Date + TimeSlot combination.
            entity.HasIndex(b => new { b.CourtId, b.BookingDate, b.TimeSlotId })
                .IsUnique()
                .HasFilter("\"BookingStatus\" <> 2"); // 2 = Cancelled

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
