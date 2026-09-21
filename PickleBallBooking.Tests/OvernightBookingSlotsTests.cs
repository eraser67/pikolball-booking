using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;

namespace PickleBallBooking.Tests;

public class OvernightBookingSlotsTests
{
    private const int TestOrganizationId = 1;

    private static ApplicationDbContext CreateContext()
    {
        var context = TestDbContextFactory.CreateInMemory(Guid.NewGuid().ToString(), TestOrganizationId);

        // Seed organization
        context.Organizations.Add(new Organization
        {
            Id = TestOrganizationId,
            Name = "Pikolball Test",
            Slug = "pikolball",
            Status = OrganizationStatus.Active
        });

        // Seed 24 active time slots
        for (int i = 0; i < 24; i++)
        {
            var start = TimeSpan.FromHours(i);
            var end = (i == 23) ? TimeSpan.Zero : TimeSpan.FromHours(i + 1);
            context.TimeSlots.Add(new TimeSlot
            {
                Id = i + 1,
                StartTime = start,
                EndTime = end,
                Status = TimeSlotStatus.Active
            });
        }

        // Seed Court
        context.Courts.Add(new Court
        {
            Id = 1,
            OrganizationId = TestOrganizationId,
            Name = "Court 1",
            Status = CourtStatus.Active
        });

        // Seed 24/7 pricing
        context.Pricings.Add(new Pricing
        {
            OrganizationId = TestOrganizationId,
            DayType = DayType.Weekday,
            StartTime = TimeSpan.Zero,
            EndTime = TimeSpan.Zero, // all day
            Price = 200m,
            Status = PricingStatus.Active
        });
        context.Pricings.Add(new Pricing
        {
            OrganizationId = TestOrganizationId,
            DayType = DayType.Weekend,
            StartTime = TimeSpan.Zero,
            EndTime = TimeSpan.Zero, // all day
            Price = 250m,
            Status = PricingStatus.Active
        });

        context.SaveChanges();
        return context;
    }

    [Fact]
    public async Task ValidateContinuousSlots_SameDay_Valid()
    {
        await using var context = CreateContext();
        var service = new BookingService(context);

        // 7-8 AM (id 8), 8-9 AM (id 9), 9-10 AM (id 10)
        var (isValid, error) = await service.ValidateContinuousSlotsAsync(new List<int> { 8, 9, 10 });
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateContinuousSlots_SameDay_Gap_Fails()
    {
        await using var context = CreateContext();
        var service = new BookingService(context);

        // 7-8 AM (id 8), 9-10 AM (id 10) - gap at 8-9 AM (id 9)
        var (isValid, error) = await service.ValidateContinuousSlotsAsync(new List<int> { 8, 10 });
        Assert.False(isValid);
        Assert.Contains("continuous", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateContinuousSlots_Overnight_Continuous_Valid()
    {
        await using var context = CreateContext();
        var service = new BookingService(context);

        // Sep 22: 10-11 PM (id 23), 11 PM-12 AM (id 24)
        // Sep 23: 12-1 AM (id 1), 1-2 AM (id 2)
        var (isValid, error) = await service.ValidateContinuousSlotsAsync(new List<int> { 23, 24, 1, 2 });
        Assert.True(isValid, error);
        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateContinuousSlots_Overnight_GapAtMidnight_Fails()
    {
        await using var context = CreateContext();
        var service = new BookingService(context);

        // Sep 22: 10-11 PM (id 23) - missing id 24!
        // Sep 23: 12-1 AM (id 1)
        var (isValid, error) = await service.ValidateContinuousSlotsAsync(new List<int> { 23, 1 });
        Assert.False(isValid);
        Assert.Contains("continuous", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateContinuousSlots_Overnight_GapInMorning_Fails()
    {
        await using var context = CreateContext();
        var service = new BookingService(context);

        // Sep 22: 11 PM-12 AM (id 24)
        // Sep 23: 1-2 AM (id 2) - missing id 1 (12-1 AM)!
        var (isValid, error) = await service.ValidateContinuousSlotsAsync(new List<int> { 24, 2 });
        Assert.False(isValid);
        Assert.Contains("continuous", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateBookingWithSlots_Overnight_AssignsCorrectDatesToSlots()
    {
        await using var context = CreateContext();
        var service = new BookingService(context);

        var date = AppClock.TodayLocal.AddDays(2); // In the future
        // Overnight: 10 PM - 2 AM (slots 23, 24, 1, 2)
        var slotIds = new List<int> { 23, 24, 1, 2 };

        var result = await service.CreateBookingWithSlotsAsync(
            courtId: 1,
            bookingDate: date,
            timeSlotIds: slotIds,
            customerName: "Juan Dela Cruz",
            customerPhone: "+639123456789",
            customerEmail: "juan@example.com");

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Booking);

        var bookingId = result.Booking.Id;
        var bookingSlots = await context.BookingTimeSlots
            .Where(bts => bts.BookingId == bookingId)
            .OrderBy(bts => bts.SlotOrder)
            .ToListAsync();

        Assert.Equal(4, bookingSlots.Count);

        // Slot 23 (10-11 PM) -> date
        Assert.Equal(23, bookingSlots[0].TimeSlotId);
        Assert.Equal(date, bookingSlots[0].BookingDate);

        // Slot 24 (11 PM - 12 AM) -> date
        Assert.Equal(24, bookingSlots[1].TimeSlotId);
        Assert.Equal(date, bookingSlots[1].BookingDate);

        // Slot 1 (12-1 AM) -> date + 1
        Assert.Equal(1, bookingSlots[2].TimeSlotId);
        Assert.Equal(date.AddDays(1), bookingSlots[2].BookingDate);

        // Slot 2 (1-2 AM) -> date + 1
        Assert.Equal(2, bookingSlots[3].TimeSlotId);
        Assert.Equal(date.AddDays(1), bookingSlots[3].BookingDate);
    }

    [Fact]
    public async Task IsAvailableAsync_OvernightBooking_BlocksOverlappingSlotsOnNextDay()
    {
        await using var context = CreateContext();
        var service = new BookingService(context);

        var date = AppClock.TodayLocal.AddDays(2);
        // Create an overnight booking: 10 PM - 2 AM (slots 23, 24, 1, 2)
        var result = await service.CreateBookingWithSlotsAsync(
            courtId: 1,
            bookingDate: date,
            timeSlotIds: new List<int> { 23, 24, 1, 2 },
            customerName: "Juan Dela Cruz",
            customerPhone: "+639123456789",
            customerEmail: "juan@example.com");

        Assert.True(result.Success);

        var nextDate = date.AddDays(1);

        // Slot 1 (12 AM - 1 AM) on nextDate should be booked
        var isAvailable1 = await service.IsAvailableAsync(1, nextDate, TimeSpan.Zero, TimeSpan.FromHours(1));
        Assert.False(isAvailable1);

        // Slot 2 (1 AM - 2 AM) on nextDate should be booked
        var isAvailable2 = await service.IsAvailableAsync(1, nextDate, TimeSpan.FromHours(1), TimeSpan.FromHours(2));
        Assert.False(isAvailable2);

        // Slot 3 (2 AM - 3 AM) on nextDate should be available
        var isAvailable3 = await service.IsAvailableAsync(1, nextDate, TimeSpan.FromHours(2), TimeSpan.FromHours(3));
        Assert.True(isAvailable3);
    }

    [Fact]
    public async Task IsAvailableAsync_OvernightBooking_BlocksOverlappingSlotsOnSameDay()
    {
        await using var context = CreateContext();
        var service = new BookingService(context);

        var date = AppClock.TodayLocal.AddDays(3);
        // Create an overnight booking: 10 PM - 2 AM (slots 23, 24, 1, 2)
        var result = await service.CreateBookingWithSlotsAsync(
            courtId: 1,
            bookingDate: date,
            timeSlotIds: new List<int> { 23, 24, 1, 2 },
            customerName: "Maria Santos",
            customerPhone: "+639987654321",
            customerEmail: "maria@example.com");

        Assert.True(result.Success);

        // Slot 23 (10 PM - 11 PM) on date should be booked
        var isAvailable23 = await service.IsAvailableAsync(1, date, TimeSpan.FromHours(22), TimeSpan.FromHours(23));
        Assert.False(isAvailable23);

        // Slot 24 (11 PM - 12 AM) on date should be booked
        var isAvailable24 = await service.IsAvailableAsync(1, date, TimeSpan.FromHours(23), TimeSpan.Zero);
        Assert.False(isAvailable24);

        // Earlier slot (8 PM - 9 PM) on date should be available
        var isAvailable20 = await service.IsAvailableAsync(1, date, TimeSpan.FromHours(20), TimeSpan.FromHours(21));
        Assert.True(isAvailable20);
    }
}
