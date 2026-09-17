using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Tests;

public class OvernightPricingTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static DateOnly NextWeekday()
    {
        var d = AppClock.TodayLocal.AddDays(1);
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            d = d.AddDays(1);
        }
        return d;
    }

    private static DateOnly NextWeekend()
    {
        var d = AppClock.TodayLocal.AddDays(1);
        while (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
        {
            d = d.AddDays(1);
        }
        return d;
    }

    [Fact]
    public async Task LateNightSlot_IsPriced_WhenBandEndsAt2359()
    {
        await using var context = CreateContext();
        // Legacy bands that end at the 23:59 sentinel.
        context.Pricings.AddRange(
            new Pricing { DayType = DayType.Weekday, StartTime = TimeSpan.Zero, EndTime = new TimeSpan(18, 0, 0), Price = 250m, Status = PricingStatus.Active },
            new Pricing { DayType = DayType.Weekday, StartTime = new TimeSpan(18, 0, 0), EndTime = new TimeSpan(23, 59, 0), Price = 300m, Status = PricingStatus.Active });
        await context.SaveChangesAsync();

        var service = new BookingService(context);
        var date = NextWeekday();

        // 11:00 PM - 12:00 AM slot.
        var result = await service.CalculatePriceAsync(date, new TimeSpan(23, 0, 0), TimeSpan.Zero);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(300m, result.Price);
    }

    [Fact]
    public async Task LateNightSlot_IsPriced_WhenBandEndsAtMidnight()
    {
        await using var context = CreateContext();
        context.Pricings.AddRange(
            new Pricing { DayType = DayType.Weekday, StartTime = TimeSpan.Zero, EndTime = new TimeSpan(18, 0, 0), Price = 250m, Status = PricingStatus.Active },
            new Pricing { DayType = DayType.Weekday, StartTime = new TimeSpan(18, 0, 0), EndTime = TimeSpan.Zero, Price = 300m, Status = PricingStatus.Active });
        await context.SaveChangesAsync();

        var service = new BookingService(context);
        var result = await service.CalculatePriceAsync(NextWeekday(), new TimeSpan(23, 0, 0), TimeSpan.Zero);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(300m, result.Price);
    }

    [Fact]
    public async Task LateNightSlot_IsPriced_WhenBandEndsAt235959()
    {
        await using var context = CreateContext();
        context.Pricings.AddRange(
            new Pricing { DayType = DayType.Weekend, StartTime = new TimeSpan(18, 0, 0), EndTime = new TimeSpan(23, 59, 59), Price = 400m, Status = PricingStatus.Active });
        await context.SaveChangesAsync();

        var service = new BookingService(context);
        var result = await service.CalculatePriceAsync(NextWeekend(), new TimeSpan(23, 0, 0), TimeSpan.Zero);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(400m, result.Price);
    }

    [Fact]
    public async Task OvernightBand_CrossingMidnight_IsPricedOnItsOwnDate()
    {
        await using var context = CreateContext();
        // A band that genuinely crosses midnight: 18:00 -> 02:00, seeded for both day
        // types so the test is independent of which weekday "tomorrow" happens to be.
        foreach (var dayType in new[] { DayType.Weekday, DayType.Weekend })
        {
            context.Pricings.AddRange(
                new Pricing { DayType = dayType, StartTime = new TimeSpan(18, 0, 0), EndTime = new TimeSpan(2, 0, 0), Price = 500m, Status = PricingStatus.Active },
                new Pricing { DayType = dayType, StartTime = TimeSpan.Zero, EndTime = new TimeSpan(2, 0, 0), Price = 500m, Status = PricingStatus.Active });
        }
        await context.SaveChangesAsync();

        var service = new BookingService(context);
        var date = NextWeekday();

        // 11:00 PM - 12:00 AM on the band's own date.
        var lateNight = await service.CalculatePriceAsync(date, new TimeSpan(23, 0, 0), TimeSpan.Zero);
        Assert.True(lateNight.Success, lateNight.ErrorMessage);
        Assert.Equal(500m, lateNight.Price);

        // 12:00 AM - 01:00 AM on the next calendar day (uses that day's own bands).
        var midnight = await service.CalculatePriceAsync(date.AddDays(1), TimeSpan.Zero, new TimeSpan(1, 0, 0));
        Assert.True(midnight.Success, midnight.ErrorMessage);
        Assert.Equal(500m, midnight.Price);
    }

    [Fact]
    public async Task FullDayCoverage_AllTwentyFourHoursArePriced()
    {
        await using var context = CreateContext();
        context.Pricings.AddRange(
            new Pricing { DayType = DayType.Weekday, StartTime = TimeSpan.Zero, EndTime = new TimeSpan(18, 0, 0), Price = 250m, Status = PricingStatus.Active },
            new Pricing { DayType = DayType.Weekday, StartTime = new TimeSpan(18, 0, 0), EndTime = TimeSpan.Zero, Price = 300m, Status = PricingStatus.Active });
        await context.SaveChangesAsync();

        var service = new BookingService(context);
        var date = NextWeekday();

        // Every hourly slot across the full 24-hour day.
        for (var hour = 0; hour < 24; hour++)
        {
            var start = TimeSpan.FromHours(hour);
            var end = hour == 23 ? TimeSpan.Zero : TimeSpan.FromHours(hour + 1);

            var result = await service.CalculatePriceAsync(date, start, end);
            Assert.True(result.Success, $"Hour {hour} failed: {result.ErrorMessage}");
            Assert.Equal(hour < 18 ? 250m : 300m, result.Price);
        }
    }
}
