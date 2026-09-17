using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Tests;

public class AdminDeleteServiceTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task PricingService_DeleteAsync_RemovesRule()
    {
        await using var context = CreateContext();
        var service = new PricingService(context);

        var pricing = await service.CreateAsync(DayType.Weekday, new TimeSpan(9, 0, 0), new TimeSpan(10, 0, 0), 200m);

        var deleted = await service.DeleteAsync(pricing.Id);

        Assert.True(deleted);
        Assert.Null(await service.GetByIdAsync(pricing.Id));
    }

    [Fact]
    public async Task PricingService_DeleteAsync_ReturnsFalseForUnknownId()
    {
        await using var context = CreateContext();
        var service = new PricingService(context);

        Assert.False(await service.DeleteAsync(999));
    }

    [Fact]
    public async Task CourtService_DeleteAsync_RemovesUnusedCourt()
    {
        await using var context = CreateContext();
        var service = new CourtService(context);

        var court = await service.CreateAsync("Court X", "Unused");

        var deleted = await service.DeleteAsync(court.Id);

        Assert.True(deleted);
        Assert.Null(await service.GetByIdAsync(court.Id));
    }

    [Fact]
    public async Task CourtService_DeleteAsync_ThrowsWhenCourtHasBookings()
    {
        await using var context = CreateContext();
        var service = new CourtService(context);

        var court = await service.CreateAsync("Court Y", null);
        var slot = new TimeSlot { Id = 1, StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(10, 0, 0), Status = TimeSlotStatus.Active };
        context.TimeSlots.Add(slot);
        context.BookingTimeSlots.Add(new BookingTimeSlot
        {
            BookingId = 1,
            CourtId = court.Id,
            BookingDate = AppClock.TodayLocal,
            TimeSlotId = slot.Id,
            SlotOrder = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(court.Id));
        Assert.NotNull(await service.GetByIdAsync(court.Id));
    }

    [Fact]
    public async Task TimeSlotService_DeleteAsync_RemovesUnusedSlotAndItsCourtConfig()
    {
        await using var context = CreateContext();
        var courtService = new CourtService(context);
        var service = new TimeSlotService(context);

        var court = await courtService.CreateAsync("Court Z", null);
        var slot = await service.CreateAsync(new TimeSpan(9, 0, 0), new TimeSpan(10, 0, 0));

        context.CourtTimeSlots.Add(new CourtTimeSlot
        {
            CourtId = court.Id,
            TimeSlotId = slot.Id,
            AvailabilityStatus = CourtTimeSlotStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var deleted = await service.DeleteAsync(slot.Id);

        Assert.True(deleted);
        Assert.Null(await service.GetByIdAsync(slot.Id));
        Assert.Empty(context.CourtTimeSlots);
    }

    [Fact]
    public async Task TimeSlotService_DeleteAsync_ThrowsWhenSlotHasBookings()
    {
        await using var context = CreateContext();
        var service = new TimeSlotService(context);

        var slot = await service.CreateAsync(new TimeSpan(9, 0, 0), new TimeSpan(10, 0, 0));
        context.BookingTimeSlots.Add(new BookingTimeSlot
        {
            BookingId = 1,
            CourtId = 1,
            BookingDate = AppClock.TodayLocal,
            TimeSlotId = slot.Id,
            SlotOrder = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(slot.Id));
        Assert.NotNull(await service.GetByIdAsync(slot.Id));
    }
}
