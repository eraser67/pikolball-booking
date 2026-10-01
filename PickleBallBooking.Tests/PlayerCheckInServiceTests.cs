using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using Xunit;

namespace PickleBallBooking.Tests;

public class PlayerCheckInServiceTests
{
    private sealed class FakeCourtImageStorage : ICourtImageStorage
    {
        public Task<string> UploadAsync(int organizationId, int courtId, IFormFile file, CancellationToken ct = default) => Task.FromResult("path");
        public Task<string> UploadLogoAsync(int organizationId, IFormFile file, CancellationToken ct = default) => Task.FromResult("path");
        public Task<string> UploadHeroImageAsync(int organizationId, IFormFile file, CancellationToken ct = default) => Task.FromResult("path");
        public Task<string> UploadAvatarAsync(string userId, IFormFile file, CancellationToken ct = default) => Task.FromResult("path");
        public Task DeleteAsync(string storagePath, CancellationToken ct = default) => Task.CompletedTask;
        public string? GetPublicUrl(string? storagePath) => storagePath is not null ? $"https://storage.test/{storagePath}" : null;
    }

    private static (PlayerCheckInService Service, AppNotificationService NotifService) CreateService(ApplicationDbContext ctx)
    {
        var notifService = new AppNotificationService(ctx, NullLogger<AppNotificationService>.Instance);
        var storage = new FakeCourtImageStorage();
        var service = new PlayerCheckInService(ctx, notifService, storage, NullLogger<PlayerCheckInService>.Instance);
        return (service, notifService);
    }

    // ── RSVP Check-In Tests ──────────────────────────────────────────────────

    [Fact]
    public async Task CheckInRsvp_ConfirmedRsvp_SuccessfullyChecksInAndNotifies()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("checkin_rsvp_success", organizationId: 1);
        var (service, _) = CreateService(ctx);

        var activity = new Activity
        {
            OrganizationId = 1,
            Name = "Morning Ladder",
            Date = DateOnly.FromDateTime(DateTime.Today),
            StartTime = TimeSpan.FromHours(8),
            EndTime = TimeSpan.FromHours(10),
            MaxCapacity = 8,
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(activity);
        await ctx.SaveChangesAsync();

        var rsvp = new ActivityRsvp
        {
            OrganizationId = 1,
            ActivityId = activity.Id,
            UserId = "user-player-1",
            Status = RsvpStatus.Confirmed
        };
        ctx.ActivityRsvps.Add(rsvp);
        await ctx.SaveChangesAsync();

        var result = await service.CheckInRsvpAsync(rsvp.Id, CheckInMethod.QrScan, "staff-admin");

        Assert.True(result.Success);
        Assert.Equal(CheckInItemType.ActivityRsvp, result.ItemType);
        Assert.Equal(RsvpStatus.CheckedIn, result.RsvpStatus);
        Assert.NotNull(result.CheckedInAt);

        // Verify in DB
        var updated = await ctx.ActivityRsvps.FindAsync(rsvp.Id);
        Assert.NotNull(updated);
        Assert.Equal(RsvpStatus.CheckedIn, updated.Status);
        Assert.Equal("staff-admin", updated.CheckedInByUserId);
        Assert.Equal(CheckInMethod.QrScan, updated.CheckInMethod);
        Assert.NotNull(updated.CheckedInAt);

        // Verify notification was sent
        var notifications = await ctx.AppNotifications.Where(n => n.UserId == "user-player-1").ToListAsync();
        Assert.Single(notifications);
        Assert.Equal(AppNotificationType.ActivityCheckedIn, notifications[0].Type);
    }

    [Fact]
    public async Task CheckInRsvp_AlreadyCheckedIn_ReturnsError()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("checkin_rsvp_already", organizationId: 1);
        var (service, _) = CreateService(ctx);

        var rsvp = new ActivityRsvp
        {
            OrganizationId = 1,
            ActivityId = 10,
            UserId = "user-1",
            Status = RsvpStatus.CheckedIn,
            CheckedInAt = DateTime.UtcNow
        };
        ctx.ActivityRsvps.Add(rsvp);
        await ctx.SaveChangesAsync();

        var result = await service.CheckInRsvpAsync(rsvp.Id, CheckInMethod.AdminManual, "staff-1");

        Assert.False(result.Success);
        Assert.Contains("already checked in", result.Message);
    }

    [Fact]
    public async Task CheckInRsvp_CancelledRsvp_ReturnsError()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("checkin_rsvp_cancelled", organizationId: 1);
        var (service, _) = CreateService(ctx);

        var rsvp = new ActivityRsvp
        {
            OrganizationId = 1,
            ActivityId = 10,
            UserId = "user-1",
            Status = RsvpStatus.Cancelled
        };
        ctx.ActivityRsvps.Add(rsvp);
        await ctx.SaveChangesAsync();

        var result = await service.CheckInRsvpAsync(rsvp.Id, CheckInMethod.AdminManual, "staff-1");

        Assert.False(result.Success);
        Assert.Contains("cancelled", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MarkRsvpNoShow_ConfirmedRsvp_SuccessfullyMarksNoShow()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("rsvp_noshow_success", organizationId: 1);
        var (service, _) = CreateService(ctx);

        var rsvp = new ActivityRsvp
        {
            OrganizationId = 1,
            ActivityId = 10,
            UserId = "user-1",
            Status = RsvpStatus.Confirmed
        };
        ctx.ActivityRsvps.Add(rsvp);
        await ctx.SaveChangesAsync();

        var result = await service.MarkRsvpNoShowAsync(rsvp.Id, "staff-1");

        Assert.True(result.Success);
        Assert.Equal(RsvpStatus.NoShow, result.RsvpStatus);

        var updated = await ctx.ActivityRsvps.FindAsync(rsvp.Id);
        Assert.Equal(RsvpStatus.NoShow, updated!.Status);
        Assert.Null(updated.CheckedInAt);
    }

    [Fact]
    public async Task UndoRsvpCheckIn_CheckedInRsvp_RestoresToConfirmed()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("rsvp_undo_checkin", organizationId: 1);
        var (service, _) = CreateService(ctx);

        var rsvp = new ActivityRsvp
        {
            OrganizationId = 1,
            ActivityId = 10,
            UserId = "user-1",
            Status = RsvpStatus.CheckedIn,
            CheckedInAt = DateTime.UtcNow,
            CheckedInByUserId = "staff-1",
            CheckInMethod = CheckInMethod.QrScan
        };
        ctx.ActivityRsvps.Add(rsvp);
        await ctx.SaveChangesAsync();

        var result = await service.UndoRsvpCheckInAsync(rsvp.Id, "staff-1");

        Assert.True(result.Success);
        Assert.Equal(RsvpStatus.Confirmed, result.RsvpStatus);
        Assert.Null(result.CheckedInAt);

        var updated = await ctx.ActivityRsvps.FindAsync(rsvp.Id);
        Assert.Equal(RsvpStatus.Confirmed, updated!.Status);
        Assert.Null(updated.CheckedInAt);
        Assert.Null(updated.CheckedInByUserId);
        Assert.Null(updated.CheckInMethod);
    }

    // ── Booking Check-In Tests ────────────────────────────────────────────────

    [Fact]
    public async Task CheckInBooking_ConfirmedBooking_SuccessfullyChecksIn()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("booking_checkin_success", organizationId: 1);
        var (service, _) = CreateService(ctx);

        var court = new Court { Id = 1, OrganizationId = 1, Name = "Court Alpha", Status = CourtStatus.Active };
        ctx.Courts.Add(court);
        var booking = new Booking
        {
            OrganizationId = 1,
            CourtId = 1,
            CustomerName = "John Doe",
            CustomerEmail = "john@example.com",
            BookingDate = DateOnly.FromDateTime(DateTime.Today),
            StartTime = TimeSpan.FromHours(10),
            EndTime = TimeSpan.FromHours(11),
            BookingStatus = BookingStatus.Confirmed,
            BookingReference = "PB-100201",
            Price = 400m
        };
        ctx.Bookings.Add(booking);
        await ctx.SaveChangesAsync();

        var result = await service.CheckInBookingAsync("PB-100201", CheckInMethod.AdminManual, "staff-1");

        Assert.True(result.Success);
        Assert.Equal(CheckInItemType.CourtBooking, result.ItemType);
        Assert.NotNull(result.CheckedInAt);

        var updated = await ctx.Bookings.FirstAsync(b => b.BookingReference == "PB-100201");
        Assert.NotNull(updated.CheckedInAt);
        Assert.Equal("staff-1", updated.CheckedInByUserId);
        Assert.Equal(CheckInMethod.AdminManual, updated.CheckInMethod);
        Assert.False(updated.IsNoShow);
    }

    [Fact]
    public async Task MarkBookingNoShow_ConfirmedBooking_MarksNoShowAndClearsCheckIn()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("booking_noshow_success", organizationId: 1);
        var (service, _) = CreateService(ctx);

        var booking = new Booking
        {
            OrganizationId = 1,
            CustomerName = "Jane Doe",
            CustomerEmail = "jane@example.com",
            BookingDate = DateOnly.FromDateTime(DateTime.Today),
            StartTime = TimeSpan.FromHours(11),
            EndTime = TimeSpan.FromHours(12),
            BookingStatus = BookingStatus.Confirmed,
            BookingReference = "PB-100202",
            CheckedInAt = DateTime.UtcNow,
            CheckInMethod = CheckInMethod.QrScan
        };
        ctx.Bookings.Add(booking);
        await ctx.SaveChangesAsync();

        var result = await service.MarkBookingNoShowAsync("PB-100202", "staff-1");

        Assert.True(result.Success);
        Assert.True(result.BookingIsNoShow);

        var updated = await ctx.Bookings.FirstAsync(b => b.BookingReference == "PB-100202");
        Assert.True(updated.IsNoShow);
        Assert.Null(updated.CheckedInAt);
    }

    [Fact]
    public async Task UndoBookingCheckIn_RestoresStatus()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("booking_undo_checkin", organizationId: 1);
        var (service, _) = CreateService(ctx);

        var booking = new Booking
        {
            OrganizationId = 1,
            CustomerName = "Jane Doe",
            CustomerEmail = "jane@example.com",
            BookingDate = DateOnly.FromDateTime(DateTime.Today),
            StartTime = TimeSpan.FromHours(11),
            EndTime = TimeSpan.FromHours(12),
            BookingStatus = BookingStatus.Confirmed,
            BookingReference = "PB-100203",
            CheckedInAt = DateTime.UtcNow,
            IsNoShow = false
        };
        ctx.Bookings.Add(booking);
        await ctx.SaveChangesAsync();

        var result = await service.UndoBookingCheckInAsync("PB-100203", "staff-1");

        Assert.True(result.Success);
        Assert.False(result.BookingIsNoShow);

        var updated = await ctx.Bookings.FirstAsync(b => b.BookingReference == "PB-100203");
        Assert.Null(updated.CheckedInAt);
        Assert.False(updated.IsNoShow);
    }

    // ── QR Code Processing & Security Tests ──────────────────────────────────

    [Fact]
    public async Task ProcessQrCode_RsvpFormat_SuccessfullyChecksIn()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("qr_rsvp_format", organizationId: 1);
        var (service, _) = CreateService(ctx);

        var rsvp = new ActivityRsvp
        {
            Id = 55,
            OrganizationId = 1,
            ActivityId = 1,
            UserId = "user-qr-1",
            Status = RsvpStatus.Confirmed
        };
        ctx.ActivityRsvps.Add(rsvp);
        await ctx.SaveChangesAsync();

        var result = await service.ProcessQrCodeAsync("RSVP:55", "staff-qr", organizationId: 1);

        Assert.True(result.Success);
        Assert.Equal(CheckInItemType.ActivityRsvp, result.ItemType);
        Assert.Equal(55, result.ItemId);
    }

    [Fact]
    public async Task ProcessQrCode_BookingFormat_SuccessfullyChecksIn()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("qr_booking_format", organizationId: 1);
        var (service, _) = CreateService(ctx);

        var booking = new Booking
        {
            OrganizationId = 1,
            CustomerName = "Alice",
            CustomerEmail = "alice@example.com",
            BookingDate = DateOnly.FromDateTime(DateTime.Today),
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(10),
            BookingStatus = BookingStatus.Confirmed,
            BookingReference = "PB-888999"
        };
        ctx.Bookings.Add(booking);
        await ctx.SaveChangesAsync();

        var result = await service.ProcessQrCodeAsync("BOOKING:PB-888999", "staff-qr", organizationId: 1);

        Assert.True(result.Success);
        Assert.Equal(CheckInItemType.CourtBooking, result.ItemType);
        Assert.Equal("PB-888999", result.Reference);
    }

    [Fact]
    public async Task ProcessQrCode_CrossTenantRsvp_RejectsAccess()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("qr_cross_tenant_rsvp", organizationId: 1);
        var (service, _) = CreateService(ctx);

        // RSVP belongs to Org 2
        ctx.SuppressTenantWriteGuard = true;
        var rsvp = new ActivityRsvp
        {
            Id = 99,
            OrganizationId = 2,
            ActivityId = 5,
            UserId = "user-org2",
            Status = RsvpStatus.Confirmed
        };
        ctx.ActivityRsvps.Add(rsvp);
        await ctx.SaveChangesAsync();
        ctx.SuppressTenantWriteGuard = false;

        // Staff from Org 1 attempts to scan Org 2's RSVP
        var result = await service.ProcessQrCodeAsync("RSVP:99", "staff-org1", organizationId: 1);

        Assert.False(result.Success);
        Assert.Contains("does not belong to this venue", result.Message);

        // Verify status was NOT modified
        var intact = await ctx.ActivityRsvps.FindAsync(99);
        Assert.Equal(RsvpStatus.Confirmed, intact!.Status);
    }

    [Fact]
    public async Task ProcessQrCode_CrossTenantBooking_RejectsAccess()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("qr_cross_tenant_booking", organizationId: 1);
        var (service, _) = CreateService(ctx);

        // Booking belongs to Org 2
        ctx.SuppressTenantWriteGuard = true;
        var booking = new Booking
        {
            OrganizationId = 2,
            CustomerName = "Bob",
            CustomerEmail = "bob@example.com",
            BookingDate = DateOnly.FromDateTime(DateTime.Today),
            StartTime = TimeSpan.FromHours(14),
            EndTime = TimeSpan.FromHours(15),
            BookingStatus = BookingStatus.Confirmed,
            BookingReference = "PB-999000"
        };
        ctx.Bookings.Add(booking);
        await ctx.SaveChangesAsync();
        ctx.SuppressTenantWriteGuard = false;

        // Staff from Org 1 attempts to scan Org 2's booking
        var result = await service.ProcessQrCodeAsync("BOOKING:PB-999000", "staff-org1", organizationId: 1);

        Assert.False(result.Success);
        Assert.Contains("does not belong to this venue", result.Message);

        var intact = await ctx.Bookings.IgnoreQueryFilters().FirstAsync(b => b.BookingReference == "PB-999000");
        Assert.Null(intact.CheckedInAt);
    }

    [Fact]
    public async Task ProcessQrCode_EmptyPayload_ReturnsFailure()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("qr_empty_payload", organizationId: 1);
        var (service, _) = CreateService(ctx);

        var result = await service.ProcessQrCodeAsync("   ", "staff-1", organizationId: 1);

        Assert.False(result.Success);
        Assert.Contains("empty", result.Message);
    }

    // ── Attendance Statistics Tests ──────────────────────────────────────────

    [Fact]
    public async Task GetPlayerAttendanceStats_CalculatesAccurately()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("attendance_stats_calc", organizationId: 1);
        var (service, _) = CreateService(ctx);

        const string userId = "stats-player-1";

        // 3 CheckedIn, 1 NoShow, 1 Confirmed (Pending)
        ctx.ActivityRsvps.AddRange(
            new ActivityRsvp { OrganizationId = 1, ActivityId = 1, UserId = userId, Status = RsvpStatus.CheckedIn },
            new ActivityRsvp { OrganizationId = 1, ActivityId = 2, UserId = userId, Status = RsvpStatus.CheckedIn },
            new ActivityRsvp { OrganizationId = 1, ActivityId = 3, UserId = userId, Status = RsvpStatus.CheckedIn },
            new ActivityRsvp { OrganizationId = 1, ActivityId = 4, UserId = userId, Status = RsvpStatus.NoShow },
            new ActivityRsvp { OrganizationId = 1, ActivityId = 5, UserId = userId, Status = RsvpStatus.Confirmed },
            new ActivityRsvp { OrganizationId = 1, ActivityId = 6, UserId = userId, Status = RsvpStatus.Cancelled } // Should be ignored
        );
        await ctx.SaveChangesAsync();

        var stats = await service.GetPlayerAttendanceStatsAsync(userId, organizationId: 1);

        Assert.Equal(5, stats.TotalActivities);
        Assert.Equal(3, stats.CheckedInCount);
        Assert.Equal(1, stats.NoShowCount);
        Assert.Equal(1, stats.PendingCount);
        // Completed = 3 checked in + 1 no show = 4. Rate = 3 / 4 = 75.0%
        Assert.Equal(75.0, stats.AttendanceRate);
    }

    [Fact]
    public async Task GetPlayerAttendanceStats_NoActivities_ReturnsZeroRateWithoutDivisionByZero()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("attendance_stats_zero", organizationId: 1);
        var (service, _) = CreateService(ctx);

        var stats = await service.GetPlayerAttendanceStatsAsync("non-existent-user", organizationId: 1);

        Assert.Equal(0, stats.TotalActivities);
        Assert.Equal(0, stats.CheckedInCount);
        Assert.Equal(0, stats.NoShowCount);
        Assert.Equal(0, stats.AttendanceRate);
    }

    // ── Today's Check-In Feed Tests ──────────────────────────────────────────

    [Fact]
    public async Task GetTodayCheckInFeed_AggregatesActivitiesAndBookings()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("today_feed_test", organizationId: 1);
        var (service, _) = CreateService(ctx);

        var today = DateOnly.FromDateTime(DateTime.Today);

        // Activity today
        var activity = new Activity
        {
            OrganizationId = 1,
            Name = "Afternoon Drop-In",
            Date = today,
            StartTime = TimeSpan.FromHours(13),
            EndTime = TimeSpan.FromHours(15),
            MaxCapacity = 4,
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(activity);
        await ctx.SaveChangesAsync();

        // 2 RSVPs for activity: 1 checked in, 1 pending
        ctx.ActivityRsvps.AddRange(
            new ActivityRsvp { OrganizationId = 1, ActivityId = activity.Id, UserId = "p1", Status = RsvpStatus.CheckedIn, CheckedInAt = DateTime.UtcNow },
            new ActivityRsvp { OrganizationId = 1, ActivityId = activity.Id, UserId = "p2", Status = RsvpStatus.Confirmed }
        );

        // 1 Booking today: checked in
        ctx.Bookings.Add(new Booking
        {
            OrganizationId = 1,
            CustomerName = "Court Player",
            CustomerEmail = "cp@example.com",
            BookingDate = today,
            StartTime = TimeSpan.FromHours(16),
            EndTime = TimeSpan.FromHours(17),
            BookingStatus = BookingStatus.Confirmed,
            BookingReference = "PB-FEED01",
            CheckedInAt = DateTime.UtcNow
        });

        await ctx.SaveChangesAsync();

        var feed = await service.GetTodayCheckInFeedAsync(organizationId: 1, date: today);

        Assert.NotNull(feed);
        Assert.Equal(today, feed.Date);
        Assert.Equal(3, feed.TotalExpected);    // 2 RSVP + 1 Booking
        Assert.Equal(2, feed.TotalCheckedIn);   // 1 RSVP + 1 Booking
        Assert.Equal(0, feed.TotalNoShow);
        Assert.Equal(1, feed.TotalPending);     // 1 RSVP pending
        Assert.Single(feed.Activities);
        Assert.Single(feed.Bookings);
    }
}
