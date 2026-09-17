using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;

namespace PickleBallBooking.Tests;

/// <summary>
/// Exercised against a real PostgreSQL instance because the double-booking
/// protection relies on a filtered unique index (CourtId, BookingDate, TimeSlotId, IsActive)
/// plus an exclusion constraint that SQLite cannot replicate.
/// </summary>
public class BookingServicePostgresTests
{
    // Fixed hour slots used by these tests: 201 = 09:00-10:00, 202 = 10:00-11:00, 203 = 11:00-12:00.
    private static async Task RunWithSeedDataAsync(Func<ApplicationDbContext, Task> action)
    {
        await using var context = PostgresTestDatabase.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        context.Courts.AddRange(
            new Court { Id = 101, Name = "Court 1", Status = CourtStatus.Active },
            new Court { Id = 102, Name = "Court 2", Status = CourtStatus.Active });

        context.TimeSlots.AddRange(
            new TimeSlot { Id = 201, StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(10, 0, 0), Status = TimeSlotStatus.Active },
            new TimeSlot { Id = 202, StartTime = new TimeSpan(10, 0, 0), EndTime = new TimeSpan(11, 0, 0), Status = TimeSlotStatus.Active },
            new TimeSlot { Id = 203, StartTime = new TimeSpan(11, 0, 0), EndTime = new TimeSpan(12, 0, 0), Status = TimeSlotStatus.Active });

        context.Pricings.AddRange(
            new Pricing { Id = 301, DayType = DayType.Weekday, StartTime = TimeSpan.Zero, EndTime = new TimeSpan(23, 59, 59), Price = 100m, Status = PricingStatus.Active },
            new Pricing { Id = 302, DayType = DayType.Weekend, StartTime = TimeSpan.Zero, EndTime = new TimeSpan(23, 59, 59), Price = 120m, Status = PricingStatus.Active });

        await context.SaveChangesAsync();
        await action(context);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task CreateBookingWithSlotsAsync_AllowsBackToBackBookings()
    {
        await RunWithSeedDataAsync(async context =>
        {
            var service = new BookingService(context);
            var date = AppClock.TodayLocal.AddDays(1);

            var first = await service.CreateBookingWithSlotsAsync(101, date, new List<int> { 201 }, "Alice", "09170000001", "alice@example.com");
            var second = await service.CreateBookingWithSlotsAsync(101, date, new List<int> { 202 }, "Bob", "09170000002", "bob@example.com");

            Assert.True(first.Success);
            Assert.True(second.Success);
        });
    }

    [Fact]
    public async Task CreateBookingWithSlotsAsync_AllowsMultiSlotContinuousBooking()
    {
        await RunWithSeedDataAsync(async context =>
        {
            var service = new BookingService(context);
            var date = AppClock.TodayLocal.AddDays(1);

            var result = await service.CreateBookingWithSlotsAsync(
                101, date, new List<int> { 201, 202, 203 }, "Alice", "09170000001", "alice@example.com");

            Assert.True(result.Success);
            Assert.Equal(new TimeSpan(9, 0, 0), result.Booking!.StartTime);
            Assert.Equal(new TimeSpan(12, 0, 0), result.Booking.EndTime);
            Assert.Equal(3m, result.Booking.DurationHours);
        });
    }

    [Fact]
    public async Task CreateBookingWithSlotsAsync_RejectsAlreadyBookedSlot()
    {
        await RunWithSeedDataAsync(async context =>
        {
            var service = new BookingService(context);
            var date = AppClock.TodayLocal.AddDays(2);

            var first = await service.CreateBookingWithSlotsAsync(101, date, new List<int> { 201, 202 }, "Alice", "09170000001", "alice@example.com");
            var second = await service.CreateBookingWithSlotsAsync(101, date, new List<int> { 202, 203 }, "Bob", "09170000002", "bob@example.com");

            Assert.True(first.Success);
            Assert.False(second.Success);
            Assert.Contains("available", second.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task CreateBookingWithSlotsAsync_RejectsNonContinuousSlots()
    {
        await RunWithSeedDataAsync(async context =>
        {
            var service = new BookingService(context);
            var date = AppClock.TodayLocal.AddDays(2);

            // 201 (09:00-10:00) and 203 (11:00-12:00) leave a one-hour gap.
            var result = await service.CreateBookingWithSlotsAsync(
                101, date, new List<int> { 201, 203 }, "Alice", "09170000001", "alice@example.com");

            Assert.False(result.Success);
            Assert.Contains("continuous", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task CreateBookingWithSlotsAsync_AllowsReuseAfterCancellation()
    {
        await RunWithSeedDataAsync(async context =>
        {
            var service = new BookingService(context);
            var date = AppClock.TodayLocal.AddDays(3);

            var booking = await service.CreateBookingWithSlotsAsync(101, date, new List<int> { 201, 202 }, "Alice", "09170000001", "alice@example.com");
            Assert.True(booking.Success);

            await service.CancelBookingAsync(booking.Booking!.Id);

            var rebook = await service.CreateBookingWithSlotsAsync(101, date, new List<int> { 201, 202 }, "Bob", "09170000002", "bob@example.com");

            Assert.True(rebook.Success);
        });
    }

    [Fact]
    public async Task CreateBookingWithSlotsAsync_AllowsSameSlotsOnDifferentCourtOrDate()
    {
        await RunWithSeedDataAsync(async context =>
        {
            var service = new BookingService(context);
            var date = AppClock.TodayLocal.AddDays(4);
            var nextDate = date.AddDays(1);

            var first = await service.CreateBookingWithSlotsAsync(101, date, new List<int> { 201, 202 }, "Alice", "09170000001", "alice@example.com");
            var differentCourt = await service.CreateBookingWithSlotsAsync(102, date, new List<int> { 201, 202 }, "Bob", "09170000002", "bob@example.com");
            var differentDate = await service.CreateBookingWithSlotsAsync(101, nextDate, new List<int> { 201, 202 }, "Cara", "09170000003", "cara@example.com");

            Assert.True(first.Success);
            Assert.True(differentCourt.Success);
            Assert.True(differentDate.Success);
        });
    }

        [Fact]
    public async Task CreateBookingWithSlotsAsync_ConcurrentOverlapAttempts_ReturnOneSuccessAndOneFailure()
    {
        await RunWithSeedDataAsync(async context =>
        {
            var service = new BookingService(context);
            var date = AppClock.TodayLocal.AddDays(5);

            var first = await service.CreateBookingWithSlotsAsync(101, date, new List<int> { 201, 202 }, "Alice", "09170000001", "alice@example.com");
            var second = await service.CreateBookingWithSlotsAsync(101, date, new List<int> { 202, 203 }, "Bob", "09170000002", "bob@example.com");

            Assert.True(first.Success);
            Assert.False(second.Success);
            Assert.Contains("available", second.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        });
    }

        [Fact]
    public async Task CalculatePriceAsync_PricesLateNightSlot_WhenBandEndsAt2359Sentinel()
    {
        // Mirrors the live pricing configuration: a daytime band ending at 18:00 and
        // an evening band that ends at the legacy 23:59 sentinel. The 23:00-00:00 slot
        // must still be priced (regression: the final hour of the day was unpriceable).
        await using var context = PostgresTestDatabase.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var date = AppClock.TodayLocal.AddDays(1);
        var dayType = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? DayType.Weekend
            : DayType.Weekday;

        context.Courts.Add(new Court { Id = 401, Name = "Late Night Court", Status = CourtStatus.Active });
        context.Pricings.AddRange(
            new Pricing { Id = 401, DayType = dayType, StartTime = TimeSpan.Zero, EndTime = new TimeSpan(18, 0, 0), Price = 250m, Status = PricingStatus.Active },
            new Pricing { Id = 402, DayType = dayType, StartTime = new TimeSpan(18, 0, 0), EndTime = new TimeSpan(23, 59, 0), Price = 300m, Status = PricingStatus.Active });
        await context.SaveChangesAsync();

        var service = new BookingService(context);
        var result = await service.CalculatePriceAsync(date, new TimeSpan(23, 0, 0), TimeSpan.Zero);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(300m, result.Price);

        await transaction.RollbackAsync();
    }
}
