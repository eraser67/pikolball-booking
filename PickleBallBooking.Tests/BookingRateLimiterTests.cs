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
    public void IsAllowed_UnderLimit_AllowsUpToTwoBookings()
    {
        var limiter = new BookingRateLimiter(); // defaults: maxBookings=2, window=15 min
        var ip = "203.0.113.19";

        // First 2 should both be allowed
        Assert.True(limiter.IsAllowed(ip, out var retryAfter1));
        Assert.Equal(TimeSpan.Zero, retryAfter1);
        limiter.RecordAttempt(ip);

        Assert.True(limiter.IsAllowed(ip, out var retryAfter2));
        Assert.Equal(TimeSpan.Zero, retryAfter2);
        limiter.RecordAttempt(ip);

        // 3rd attempt must be rejected
        Assert.False(limiter.IsAllowed(ip, out var retryAfter3));
        Assert.True(retryAfter3 > TimeSpan.Zero);
        Assert.True(retryAfter3 <= TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void IsAllowed_DifferentIps_TrackIndependently()
    {
        var limiter = new BookingRateLimiter();
        var ipSpammer = "198.51.100.1";
        var ipLegit = "198.51.100.2";

        // Spammer hits 2 bookings
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
        var limiter = new BookingRateLimiter(); // defaults: maxBookings=2, window=15 min
        var testIp = "49.145.2.10";

        // Pre-record 2 attempts for this IP
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
        Assert.Contains("limited to 2 per 15 minutes", model.ErrorMessage);
        Assert.Empty(db.Bookings);
    }
}
