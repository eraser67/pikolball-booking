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
}
