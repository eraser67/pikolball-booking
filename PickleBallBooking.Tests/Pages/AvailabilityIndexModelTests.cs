using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Pages.Availability;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using Xunit;

namespace PickleBallBooking.Tests.Pages;

public class AvailabilityIndexModelTests
{
    // Phase 21: the fixtures below are tenant-owned and attached to org id 1, so the
    // context must resolve to that same organization.
    private const int TestOrganizationId = 1;

    private static ApplicationDbContext CreateContext()
    {
        return TestDbContextFactory.CreateInMemory(Guid.NewGuid().ToString(), TestOrganizationId);
    }

    private static async Task<ApplicationDbContext> SeedAsync()
    {
        var context = CreateContext();

                // Phase 20.5: tenant-owned fixture rows carry an OrganizationId for parity with
        // the PostgreSQL schema (InMemory does not enforce the FK).
        context.Courts.AddRange(
            new Court { Id = 1, OrganizationId = 1, Name = "Court 1", Status = CourtStatus.Active },
            new Court { Id = 2, OrganizationId = 1, Name = "Court 2", Status = CourtStatus.Active },
            new Court { Id = 3, OrganizationId = 1, Name = "Court 3", Status = CourtStatus.Inactive });

        context.TimeSlots.AddRange(
            new TimeSlot { Id = 1, StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(10, 0, 0), Status = TimeSlotStatus.Active },
            new TimeSlot { Id = 2, StartTime = new TimeSpan(10, 0, 0), EndTime = new TimeSpan(11, 0, 0), Status = TimeSlotStatus.Active });

                context.Pricings.AddRange(
            new Pricing { Id = 1, OrganizationId = 1, DayType = DayType.Weekday, StartTime = TimeSpan.Zero, EndTime = new TimeSpan(23, 59, 59), Price = 100m, Status = PricingStatus.Active },
            new Pricing { Id = 2, OrganizationId = 1, DayType = DayType.Weekend, StartTime = TimeSpan.Zero, EndTime = new TimeSpan(23, 59, 59), Price = 120m, Status = PricingStatus.Active });

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

        Assert.Equal(AppClock.TodayLocal, model.Date);
        Assert.False(model.IsPastDate);
        Assert.Equal(7, model.DateOptions.Count);
    }

    [Fact]
    public async Task OnGetAsync_LoadsCalendarRangesAndActiveCourts()
    {
        await using var context = await SeedAsync();
        var model = CreateModel(context);

        await model.OnGetAsync();

        Assert.Equal(2, model.Courts.Count);
        Assert.DoesNotContain(model.Courts, c => c.Status == CourtStatus.Inactive);
        // Now we only show predefined time slots (2), not all combinations (3)
        Assert.Equal(2, model.TimeSlotAvailabilities.Count);
        Assert.All(model.TimeSlotAvailabilities, slot => Assert.True(slot.HasAvailableSlots));
    }

    [Fact]
    public async Task OnGetAsync_MarksSelectedRange_WhenProvidedViaQuery()
    {
        await using var context = await SeedAsync();
        var model = CreateModel(context);

                model.Date = AppClock.TodayLocal;
        await model.OnGetAsync();

        Assert.Contains(model.TimeSlotAvailabilities, slot => slot.HasAvailableSlots);
    }

    [Fact]
    public async Task OnGetAsync_MarksPastDateAsUnavailable()
    {
        await using var context = await SeedAsync();
        var model = CreateModel(context);

        model.Date = AppClock.TodayLocal.AddDays(-1);
        await model.OnGetAsync();

        Assert.True(model.IsPastDate);
        Assert.All(model.TimeSlotAvailabilities, slot => Assert.False(slot.HasAvailableSlots));
    }

    [Theory]
    [InlineData("John Doe", "John D.")]
    [InlineData("Maria Clara Santos", "Maria S.")]
    [InlineData("Alex", "Alex")]
    [InlineData("", "Reserved")]
    [InlineData("   ", "Reserved")]
    [InlineData(null, "Reserved")]
    public void FormatBookerDisplayName_FormatsNamesCorrectly(string? input, string expected)
    {
        var result = BookingService.FormatBookerDisplayName(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task OnGetAsync_PopulatesBookerDisplayName_WhenCourtIsBooked()
    {
        await using var context = await SeedAsync();

        var today = AppClock.TodayLocal;
        var booking = new Booking
        {
            Id = 10,
            OrganizationId = 1,
            BookingReference = "PB-TEST-001",
            CustomerName = "Michael Jordan",
            CustomerPhone = "09123456789",
            CustomerEmail = "mj@example.com",
            CourtId = 1,
            BookingDate = today,
            StartTime = new TimeSpan(9, 0, 0),
            EndTime = new TimeSpan(10, 0, 0),
            Price = 100m,
            BookingStatus = BookingStatus.Confirmed
        };
        context.Bookings.Add(booking);

        var bookingSlot = new BookingTimeSlot
        {
            Id = 1,
            OrganizationId = 1,
            BookingId = 10,
            CourtId = 1,
            BookingDate = today,
            TimeSlotId = 1,
            SlotOrder = 0,
            IsActive = true
        };
        context.BookingTimeSlots.Add(bookingSlot);
        await context.SaveChangesAsync();

        var model = CreateModel(context);
        model.Date = today;
        await model.OnGetAsync();

        var slot9am = model.TimeSlotAvailabilities.FirstOrDefault(s => s.StartTime == new TimeSpan(9, 0, 0));
        Assert.NotNull(slot9am);
        Assert.Contains(slot9am.BookedCourts, c => c.Id == 1);
        Assert.Contains(slot9am.AvailableCourts, c => c.Id == 2);
        Assert.Equal("Michael J.", slot9am.GetBookerForCourt(1));
    }

    [Fact]
    public void TimePeriod_CategorizesCorrectly()
    {
        var morningSlot = new TimeSlotAvailability { StartTime = new TimeSpan(9, 0, 0) };
        var afternoonSlot = new TimeSlotAvailability { StartTime = new TimeSpan(14, 0, 0) };
        var eveningSlot = new TimeSlotAvailability { StartTime = new TimeSpan(18, 0, 0) };

        Assert.Equal("morning", morningSlot.TimePeriod);
        Assert.Equal("afternoon", afternoonSlot.TimePeriod);
        Assert.Equal("evening", eveningSlot.TimePeriod);
    }
}
