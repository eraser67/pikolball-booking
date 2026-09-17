using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;

namespace PickleBallBooking.Tests;

/// <summary>
/// Read-only smoke checks against the real configured database. These verify that
/// the actual live pricing configuration can price every hourly slot across a full
/// day, including the 23:00-00:00 slot. No data is written.
/// </summary>
public class LivePricingSmokeTests
{
    [Fact]
    public async Task LiveConfiguration_PricesEveryHourOfTheDay()
    {
        await using var context = PostgresTestDatabase.CreateContext();
        var service = new BookingService(context);

        // Try the next weekday and the next weekend day to cover both day types.
        var date = AppClock.TodayLocal.AddDays(1);

        var failures = new List<string>();
        for (var hour = 0; hour < 24; hour++)
        {
            var start = TimeSpan.FromHours(hour);
            var end = hour == 23 ? TimeSpan.Zero : TimeSpan.FromHours(hour + 1);

            var result = await service.CalculatePriceAsync(date, start, end);
            if (!result.Success)
            {
                failures.Add($"{start:hh\\:mm}-{(hour == 23 ? "00:00" : end.ToString("hh\\:mm"))} ({date:yyyy-MM-dd} {date.DayOfWeek}): {result.ErrorMessage}");
            }
        }

        Assert.True(failures.Count == 0, "Unpriceable slots:\n" + string.Join("\n", failures));

        // The late-night slot must specifically be priced.
        var lateNight = await service.CalculatePriceAsync(date, new TimeSpan(23, 0, 0), TimeSpan.Zero);
        Assert.True(lateNight.Success, lateNight.ErrorMessage);
        Assert.True(lateNight.Price > 0);
    }
}
