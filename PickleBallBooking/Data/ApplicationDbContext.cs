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

    public DbSet<BookingTimeSlot> BookingTimeSlots => Set<BookingTimeSlot>();

    public DbSet<CourtTimeSlot> CourtTimeSlots => Set<CourtTimeSlot>();

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

            // Unique constraint: prevent duplicate (CourtId, BookingDate, TimeSlotId) for active bookings
            // This prevents double-booking at the database level with transactional safety
            entity.HasIndex(bts => new { bts.CourtId, bts.BookingDate, bts.TimeSlotId, bts.IsActive })
                .IsUnique()
                .HasFilter("\"IsActive\" = true")
                .HasDatabaseName("IX_BookingTimeSlot_CourtId_BookingDate_TimeSlotId_Active");

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
        });

        // Configure CourtTimeSlot entity
        modelBuilder.Entity<CourtTimeSlot>(entity =>
        {
            entity.HasKey(cts => cts.Id);

            // Unique constraint: one court can have each time slot status defined only once
            entity.HasIndex(cts => new { cts.CourtId, cts.TimeSlotId })
                .IsUnique()
                .HasDatabaseName("IX_CourtTimeSlot_CourtId_TimeSlotId");

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
        });
    }
}
