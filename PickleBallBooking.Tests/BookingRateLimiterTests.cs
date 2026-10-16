using Microsoft.AspNetCore.Http;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Pages.Booking;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using System.Net;
using Xunit;

namespace PickleBallBooking.Tests;

public class BookingRateLimiterTests
{
    private const int TestOrgId = 1;

    [Fact]
    public void IsAllowed_UnderLimit_AllowsUpToThreeBookings()
    {
        var limiter = new BookingRateLimiter(maxBookings: 3, window: TimeSpan.FromMinutes(10));
        var ip = "203.0.113.19";

        // First 3 should all be allowed
        Assert.True(limiter.IsAllowed(ip, out var retryAfter1));
        Assert.Equal(TimeSpan.Zero, retryAfter1);
        limiter.RecordAttempt(ip);

        Assert.True(limiter.IsAllowed(ip, out var retryAfter2));
        Assert.Equal(TimeSpan.Zero, retryAfter2);
        limiter.RecordAttempt(ip);

        Assert.True(limiter.IsAllowed(ip, out var retryAfter3));
        Assert.Equal(TimeSpan.Zero, retryAfter3);
        limiter.RecordAttempt(ip);

        // 4th attempt must be rejected
        Assert.False(limiter.IsAllowed(ip, out var retryAfter4));
        Assert.True(retryAfter4 > TimeSpan.Zero);
        Assert.True(retryAfter4 <= TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void IsAllowed_DifferentIps_TrackIndependently()
    {
        var limiter = new BookingRateLimiter(maxBookings: 3, window: TimeSpan.FromMinutes(10));
        var ipSpammer = "198.51.100.1";
        var ipLegit = "198.51.100.2";

        // Spammer hits 3 bookings
        limiter.RecordAttempt(ipSpammer);
        limiter.RecordAttempt(ipSpammer);
        limiter.RecordAttempt(ipSpammer);

        Assert.False(limiter.IsAllowed(ipSpammer, out _));

        // Legit player from different IP is still allowed
        Assert.True(limiter.IsAllowed(ipLegit, out var retryAfterLegit));
        Assert.Equal(TimeSpan.Zero, retryAfterLegit);
    }

    [Fact]
    public void GetClientIp_ExtractsFirstIpFromXForwardedFor()
    {
        var limiter = new BookingRateLimiter();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = "112.198.10.5, 172.69.10.1, 10.0.0.1";

        var clientIp = limiter.GetClientIp(context);

        Assert.Equal("112.198.10.5", clientIp);
    }

    [Fact]
    public void GetClientIp_FallbackToConnectionRemoteIpAddress()
    {
        var limiter = new BookingRateLimiter();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("120.28.15.22");

        var clientIp = limiter.GetClientIp(context);

        Assert.Equal("120.28.15.22", clientIp);
    }

    [Fact]
    public async Task IndexModel_OnPostConfirmAsync_BlocksWhenRateLimitExceeded()
    {
        // Arrange
        var limiter = new BookingRateLimiter(maxBookings: 3, window: TimeSpan.FromMinutes(10));
        var testIp = "49.145.2.10";

        // Pre-record 3 attempts for this IP
        limiter.RecordAttempt(testIp);
        limiter.RecordAttempt(testIp);
        limiter.RecordAttempt(testIp);

        var db = TestDbContextFactory.CreateInMemory(Guid.NewGuid().ToString(), TestOrgId);
        var tenantContext = new TenantContext { OrganizationId = TestOrgId };
        var courtService = new CourtService(db);
        var timeSlotService = new TimeSlotService(db, tenantContext);
        var bookingService = new BookingService(db, timeSlotService: timeSlotService);
        var paymentService = new PaymentService(db, tenantContext);

        var model = new IndexModel(
            bookingService,
            courtService,
            timeSlotService,
            db,
            paymentService,
            limiter);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse(testIp);
        model.PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext
        {
            HttpContext = httpContext
        };

        model.SelectedSlotIds = new[] { 1 };
        model.Input = new IndexModel.BookingInput
        {
            CourtId = 1,
            BookingDate = AppClock.TodayLocal.AddDays(1),
            CustomerName = "Test Spammer",
            CustomerPhone = "09171234567",
            CustomerEmail = "spammer@test.com"
        };

        // Act
        var result = await model.OnPostConfirmAsync();

        // Assert: Rate limit error message should be set, and no booking should be created in the database!
        Assert.NotNull(model.ErrorMessage);
        Assert.Contains("Too many booking attempts", model.ErrorMessage);
        Assert.Contains("limited to 3 per 10 minutes", model.ErrorMessage);
        Assert.Empty(db.Bookings);
    }
}
