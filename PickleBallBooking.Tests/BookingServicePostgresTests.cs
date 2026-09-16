using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;

namespace PickleBallBooking.Tests;

public class BookingServicePostgresTests
{
    private static async Task RunWithSeedDataAsync(Func<ApplicationDbContext, Task> action)
    {
        await using var context = PostgresTestDatabase.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        context.Courts.AddRange(
            new Court { Id = 101, Name = "Court 1", Status = CourtStatus.Active },
            new Court { Id = 102, Name = "Court 2", Status = CourtStatus.Active });

        context.TimeSlots.AddRange(
            new TimeSlot { Id = 201, StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(10, 0, 0), Status = TimeSlotStatus.Active },
            new TimeSlot { Id = 202, StartTime = new TimeSpan(10, 0, 0), EndTime = new TimeSpan(11, 0, 0), Status = TimeSlotStatus.Active });

        context.Pricings.AddRange(
            new Pricing { Id = 301, DayType = DayType.Weekday, StartTime = TimeSpan.Zero, EndTime = new TimeSpan(23, 59, 59), Price = 100m, Status = PricingStatus.Active },
            new Pricing { Id = 302, DayType = DayType.Weekend, StartTime = TimeSpan.Zero, EndTime = new TimeSpan(23, 59, 59), Price = 120m, Status = PricingStatus.Active });

        await context.SaveChangesAsync();
        await action(context);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task CreateBookingAsync_AllowsBackToBackBookings()
    {
        await RunWithSeedDataAsync(async context =>
        {
            var service = new BookingService(context);
            var date = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1));

            var first = await service.CreateBookingAsync(101, date, new TimeSpan(9, 0, 0), new TimeSpan(10, 0, 0), "Alice", "09170000001", "alice@example.com");
            var second = await service.CreateBookingAsync(101, date, new TimeSpan(10, 0, 0), new TimeSpan(11, 0, 0), "Bob", "09170000002", "bob@example.com");

            Assert.True(first.Success);
            Assert.True(second.Success);
        });
    }

    [Fact]
    public async Task CreateBookingAsync_RejectsOverlappingBookingsOnSameCourtAndDate()
    {
        await RunWithSeedDataAsync(async context =>
        {
            var service = new BookingService(context);
            var date = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(2));

            var first = await service.CreateBookingAsync(101, date, new TimeSpan(9, 0, 0), new TimeSpan(11, 0, 0), "Alice", "09170000001", "alice@example.com");
            var second = await service.CreateBookingAsync(101, date, new TimeSpan(10, 0, 0), new TimeSpan(12, 0, 0), "Bob", "09170000002", "bob@example.com");

            Assert.True(first.Success);
            Assert.False(second.Success);
            Assert.Contains("available", second.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task CreateBookingAsync_AllowsCancelledOverlapOnSameCourtAndDate()
    {
        await RunWithSeedDataAsync(async context =>
        {
            var service = new BookingService(context);
            var date = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(3));

            var booking = await service.CreateBookingAsync(101, date, new TimeSpan(9, 0, 0), new TimeSpan(11, 0, 0), "Alice", "09170000001", "alice@example.com");
            Assert.True(booking.Success);

            var saved = await context.Bookings.SingleAsync(b => b.BookingReference == booking.Booking!.BookingReference);
            saved.BookingStatus = BookingStatus.Cancelled;
            await context.SaveChangesAsync();

            var overlap = await service.CreateBookingAsync(101, date, new TimeSpan(10, 0, 0), new TimeSpan(12, 0, 0), "Bob", "09170000002", "bob@example.com");

            Assert.True(overlap.Success);
        });
    }

    [Fact]
    public async Task CreateBookingAsync_AllowsSameTimeRangeOnDifferentCourtOrDate()
    {
        await RunWithSeedDataAsync(async context =>
        {
            var service = new BookingService(context);
            var date = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(4));
            var nextDate = date.AddDays(1);

            var first = await service.CreateBookingAsync(101, date, new TimeSpan(9, 0, 0), new TimeSpan(11, 0, 0), "Alice", "09170000001", "alice@example.com");
            var differentCourt = await service.CreateBookingAsync(102, date, new TimeSpan(9, 0, 0), new TimeSpan(11, 0, 0), "Bob", "09170000002", "bob@example.com");
            var differentDate = await service.CreateBookingAsync(101, nextDate, new TimeSpan(9, 0, 0), new TimeSpan(11, 0, 0), "Cara", "09170000003", "cara@example.com");

            Assert.True(first.Success);
            Assert.True(differentCourt.Success);
            Assert.True(differentDate.Success);
        });
    }

    [Fact]
    public async Task CreateBookingAsync_ConcurrentOverlapAttempts_ReturnOneSuccessAndOneFailure()
    {
        await RunWithSeedDataAsync(async context =>
        {
            var service = new BookingService(context);
            var date = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(5));

            var first = await service.CreateBookingAsync(101, date, new TimeSpan(9, 0, 0), new TimeSpan(11, 0, 0), "Alice", "09170000001", "alice@example.com");
            var second = await service.CreateBookingAsync(101, date, new TimeSpan(10, 0, 0), new TimeSpan(12, 0, 0), "Bob", "09170000002", "bob@example.com");

            Assert.True(first.Success);
            Assert.False(second.Success);
            Assert.Contains("available", second.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        });
    }
}
