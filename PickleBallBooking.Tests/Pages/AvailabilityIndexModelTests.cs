using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Pages.Availability;
using PickleBallBooking.Services;
using Xunit;

namespace PickleBallBooking.Tests.Pages;

public class AvailabilityIndexModelTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<ApplicationDbContext> SeedAsync()
    {
        var context = CreateContext();

        var court1 = new Court { Id = 1, Name = "Court 1", Status = CourtStatus.Active };
        var court2 = new Court { Id = 2, Name = "Court 2", Status = CourtStatus.Active };
        var inactiveCourt = new Court { Id = 3, Name = "Court 3", Status = CourtStatus.Inactive };

        var timeSlot1 = new TimeSlot { Id = 1, StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(10, 0, 0), Status = TimeSlotStatus.Active };
        var timeSlot2 = new TimeSlot { Id = 2, StartTime = new TimeSpan(10, 0, 0), EndTime = new TimeSpan(11, 0, 0), Status = TimeSlotStatus.Active };

        context.Courts.AddRange(court1, court2, inactiveCourt);
        context.TimeSlots.AddRange(timeSlot1, timeSlot2);

        context.Bookings.Add(new Booking
        {
            Id = 1,
            BookingReference = "REF001",
            CustomerName = "Alice",
            CustomerPhone = "0000000000",
            CustomerEmail = "alice@example.com",
            CourtId = 1,
            TimeSlotId = 1,
            BookingDate = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            Price = 100,
            BookingStatus = BookingStatus.Confirmed
        });

        context.Bookings.Add(new Booking
        {
            Id = 2,
            BookingReference = "REF002",
            CustomerName = "Bob",
            CustomerPhone = "1111111111",
            CustomerEmail = "bob@example.com",
            CourtId = 2,
            TimeSlotId = 2,
            BookingDate = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            Price = 100,
            BookingStatus = BookingStatus.Cancelled
        });

        await context.SaveChangesAsync();
        return context;
    }

    private static IndexModel CreateModel(ApplicationDbContext context)
    {
        var bookingService = new BookingService(context);
        var courtService = new CourtService(context);
        var timeSlotService = new TimeSlotService(context);

        return new IndexModel(bookingService, courtService, timeSlotService);
    }

    [Fact]
    public async Task OnGetAsync_DefaultsToToday_WhenNoDateProvided()
    {
        await using var context = await SeedAsync();
        var model = CreateModel(context);

        await model.OnGetAsync();

        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow.Date), model.Date);
        Assert.False(model.IsPastDate);
    }

    [Fact]
    public async Task OnGetAsync_OnlyIncludesActiveCourtsAndTimeSlots()
    {
        await using var context = await SeedAsync();
        var model = CreateModel(context);

        await model.OnGetAsync();

        Assert.Equal(2, model.Courts.Count);
        Assert.DoesNotContain(model.Courts, c => c.Status == CourtStatus.Inactive);
        Assert.Equal(2, model.TimeSlots.Count);
    }

    [Fact]
    public async Task IsAvailable_ReturnsFalse_ForConfirmedBookedSlot()
    {
        await using var context = await SeedAsync();
        var model = CreateModel(context);

        await model.OnGetAsync();

        Assert.False(model.IsAvailable(1, 1));
    }

    [Fact]
    public async Task IsAvailable_ReturnsTrue_ForCancelledBookingSlot()
    {
        await using var context = await SeedAsync();
        var model = CreateModel(context);

        await model.OnGetAsync();

        Assert.True(model.IsAvailable(2, 2));
    }

    [Fact]
    public async Task IsAvailable_ReturnsTrue_ForSlotWithNoBooking()
    {
        await using var context = await SeedAsync();
        var model = CreateModel(context);

        await model.OnGetAsync();

        Assert.True(model.IsAvailable(2, 1));
        Assert.True(model.IsAvailable(1, 2));
    }

    [Fact]
    public async Task IsAvailable_ReturnsFalse_ForPastDate()
    {
        await using var context = await SeedAsync();
        var model = CreateModel(context);

        model.Date = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));
        await model.OnGetAsync();

        Assert.True(model.IsPastDate);
        Assert.False(model.IsAvailable(2, 1));
    }
}
