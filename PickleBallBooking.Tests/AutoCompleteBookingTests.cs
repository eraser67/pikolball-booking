using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;

namespace PickleBallBooking.Tests;

public class AutoCompleteBookingTests
{
    // Phase 21: fixtures use a resolved tenant so query filters/write guard apply.
    private const int TestOrganizationId = 1;

    private static ApplicationDbContext CreateContext()
    {
        return TestDbContextFactory.CreateInMemory(Guid.NewGuid().ToString(), TestOrganizationId);
    }

    private static Booking NewBooking(DateOnly date, TimeSpan start, TimeSpan end, BookingStatus status)
                => new()
        {
            // Phase 20.5: tenant-owned entity; InMemory does not enforce the FK but the
            // fixture stays tenant-aware for parity with the PostgreSQL schema.
            OrganizationId = 1,
            BookingReference = $"PB-{date:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..4]}",
            CustomerName = "Test",
            CustomerPhone = "0900",
            CustomerEmail = "test@example.com",
            CourtId = 1,
            BookingDate = date,
            StartTime = start,
            EndTime = end,
            Price = 100m,
            BookingStatus = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    [Fact]
    public async Task AutoComplete_CompletesConfirmedBookingWhoseEndHasPassed()
    {
        await using var context = CreateContext();
        var yesterday = AppClock.TodayLocal.AddDays(-1);
        var booking = NewBooking(yesterday, new TimeSpan(9, 0, 0), new TimeSpan(10, 0, 0), BookingStatus.Confirmed);
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        var service = new BookingService(context);
        var completed = await service.AutoCompleteExpiredBookingsAsync();

        Assert.Equal(1, completed);
        Assert.Equal(BookingStatus.Completed, (await context.Bookings.FindAsync(booking.Id))!.BookingStatus);
    }

    [Fact]
    public async Task AutoComplete_LeavesFutureConfirmedBookingUntouched()
    {
        await using var context = CreateContext();
        var tomorrow = AppClock.TodayLocal.AddDays(1);
        var booking = NewBooking(tomorrow, new TimeSpan(9, 0, 0), new TimeSpan(10, 0, 0), BookingStatus.Confirmed);
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        var service = new BookingService(context);
        var completed = await service.AutoCompleteExpiredBookingsAsync();

        Assert.Equal(0, completed);
        Assert.Equal(BookingStatus.Confirmed, (await context.Bookings.FindAsync(booking.Id))!.BookingStatus);
    }

    [Fact]
    public async Task AutoComplete_DoesNotTouchPendingOrCancelledBookings()
    {
        await using var context = CreateContext();
        var yesterday = AppClock.TodayLocal.AddDays(-1);
        var pending = NewBooking(yesterday, new TimeSpan(9, 0, 0), new TimeSpan(10, 0, 0), BookingStatus.Pending);
        var cancelled = NewBooking(yesterday, new TimeSpan(11, 0, 0), new TimeSpan(12, 0, 0), BookingStatus.Cancelled);
        context.Bookings.AddRange(pending, cancelled);
        await context.SaveChangesAsync();

        var service = new BookingService(context);
        var completed = await service.AutoCompleteExpiredBookingsAsync();

        Assert.Equal(0, completed);
        Assert.Equal(BookingStatus.Pending, (await context.Bookings.FindAsync(pending.Id))!.BookingStatus);
        Assert.Equal(BookingStatus.Cancelled, (await context.Bookings.FindAsync(cancelled.Id))!.BookingStatus);
    }

    [Fact]
    public async Task AutoComplete_CompletesOvernightBookingOnceEndTimePassed()
    {
        await using var context = CreateContext();
        // Yesterday 23:00 - 00:00 ends at midnight last night, which is in the past.
        var yesterday = AppClock.TodayLocal.AddDays(-1);
        var booking = NewBooking(yesterday, new TimeSpan(23, 0, 0), TimeSpan.Zero, BookingStatus.Confirmed);
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        var service = new BookingService(context);
        var completed = await service.AutoCompleteExpiredBookingsAsync();

        Assert.Equal(1, completed);
        Assert.Equal(BookingStatus.Completed, (await context.Bookings.FindAsync(booking.Id))!.BookingStatus);
    }

    [Fact]
    public async Task AutoComplete_CompletesTodayBookingWhoseEndHourHasPassed()
    {
        await using var context = CreateContext();
        // A booking today that ended one hour ago (assuming now is after 01:00 local,
        // so use an early-morning window), else skip if it would still be in the future.
        var now = AppClock.NowLocal;
        var start = new TimeSpan(0, 0, 0);
        var end = new TimeSpan(1, 0, 0);

        if (now.TimeOfDay <= end)
        {
            return; // Nothing to assert at this time of day.
        }

        var booking = NewBooking(AppClock.TodayLocal, start, end, BookingStatus.Confirmed);
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        var service = new BookingService(context);
        var completed = await service.AutoCompleteExpiredBookingsAsync();

        Assert.Equal(1, completed);
        Assert.Equal(BookingStatus.Completed, (await context.Bookings.FindAsync(booking.Id))!.BookingStatus);
    }

    [Theory]
    [InlineData(9, 0, 10, 0, 0, 10)]   // same-day morning slot -> ends same day at 10:00
    [InlineData(23, 0, 0, 0, 1, 0)]    // 23:00-00:00 -> ends next day at midnight
    [InlineData(22, 0, 1, 0, 1, 1)]    // 22:00-01:00 -> ends next day at 01:00
    public void ToEndLocalDateTime_HandlesSameDayAndOvernightRanges(
        int sh, int sm, int eh, int em, int expectedDayOffset, int expectedHour)
    {
        var date = new DateOnly(2026, 1, 1);
        var end = AppClock.ToEndLocalDateTime(date, new TimeSpan(sh, sm, 0), new TimeSpan(eh, em, 0));

        Assert.Equal(date.AddDays(expectedDayOffset), DateOnly.FromDateTime(end));
        Assert.Equal(expectedHour, end.Hour);
    }
}
