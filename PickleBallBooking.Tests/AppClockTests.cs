using PickleBallBooking.Services;
using Xunit;

namespace PickleBallBooking.Tests;

public class AppClockTests
{
    [Fact]
    public void ToPhilippineTime_ConvertsUtcToUtcPlusEight()
    {
        // 2026-09-21 04:00:00 UTC should be 2026-09-21 12:00:00 (noon) Philippines time
        var utc = new DateTime(2026, 9, 21, 4, 0, 0, DateTimeKind.Utc);
        var phTime = AppClock.ToPhilippineTime(utc);

        Assert.Equal(2026, phTime.Year);
        Assert.Equal(9, phTime.Month);
        Assert.Equal(21, phTime.Day);
        Assert.Equal(12, phTime.Hour);
        Assert.Equal(0, phTime.Minute);
    }

    [Fact]
    public void ToPhilippineTime_HandlesMidnightBoundary()
    {
        // 2026-09-21 18:00:00 UTC should be 2026-09-22 02:00:00 Philippines time (next day)
        var utc = new DateTime(2026, 9, 21, 18, 0, 0, DateTimeKind.Utc);
        var phTime = AppClock.ToPhilippineTime(utc);

        Assert.Equal(2026, phTime.Year);
        Assert.Equal(9, phTime.Month);
        Assert.Equal(22, phTime.Day);
        Assert.Equal(2, phTime.Hour);
    }

    [Fact]
    public void ToPhilippineTime_Nullable_ReturnsNullForNull()
    {
        DateTime? nullUtc = null;
        var result = AppClock.ToPhilippineTime(nullUtc);

        Assert.Null(result);
    }

    [Fact]
    public void ToPhilippineTime_Nullable_ConvertsWhenValuePresent()
    {
        DateTime? utc = new DateTime(2026, 9, 21, 8, 30, 0, DateTimeKind.Utc);
        var result = AppClock.ToPhilippineTime(utc);

        Assert.NotNull(result);
        Assert.Equal(16, result.Value.Hour);
        Assert.Equal(30, result.Value.Minute);
    }
}
