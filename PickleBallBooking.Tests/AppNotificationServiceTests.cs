using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using Xunit;

namespace PickleBallBooking.Tests;

public class AppNotificationServiceTests
{
    private static AppNotificationService CreateService(ApplicationDbContext ctx)
        => new(ctx, NullLogger<AppNotificationService>.Instance);

    [Fact]
    public async Task CreateAsync_CreatesUnreadNotificationWithCorrectProperties()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("notif_create_test", organizationId: 1);
        var service = CreateService(ctx);

        await service.CreateAsync("user-1", AppNotificationType.ActivityRsvpConfirmed, "Spot Confirmed", "You got in!", "/Customer/Dashboard");

        var notifications = await service.GetForUserAsync("user-1");
        Assert.Single(notifications);
        var n = notifications[0];
        Assert.Equal("user-1", n.UserId);
        Assert.Equal(AppNotificationType.ActivityRsvpConfirmed, n.Type);
        Assert.Equal("Spot Confirmed", n.Title);
        Assert.Equal("You got in!", n.Body);
        Assert.Equal("/Customer/Dashboard", n.ActionUrl);
        Assert.False(n.IsRead);
        Assert.Null(n.ReadAt);
    }

    [Fact]
    public async Task GetUnreadCountAsync_ReturnsAccurateCountForSpecificUserOnly()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("notif_count_test", organizationId: 1);
        var service = CreateService(ctx);

        await service.CreateAsync("user-1", AppNotificationType.ActivityRsvpConfirmed, "N1");
        await service.CreateAsync("user-1", AppNotificationType.ActivityWaitlisted, "N2");
        await service.CreateAsync("user-2", AppNotificationType.ActivityRsvpConfirmed, "N3"); // other user

        var count1 = await service.GetUnreadCountAsync("user-1");
        var count2 = await service.GetUnreadCountAsync("user-2");
        Assert.Equal(2, count1);
        Assert.Equal(1, count2);

        // Mark one as read for user-1
        var list = await service.GetForUserAsync("user-1");
        await service.MarkReadAsync(list[0].Id, "user-1");

        count1 = await service.GetUnreadCountAsync("user-1");
        Assert.Equal(1, count1);
    }

    [Fact]
    public async Task GetForUserAsync_OrdersNewestFirstAndAppliesLimit()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("notif_order_test", organizationId: 1);
        var service = CreateService(ctx);

        await service.CreateAsync("user-1", AppNotificationType.ActivityRsvpConfirmed, "First");
        await service.CreateAsync("user-1", AppNotificationType.ActivityWaitlistPromoted, "Second");
        await service.CreateAsync("user-1", AppNotificationType.ActivityCancelled, "Third");

        var all = await service.GetForUserAsync("user-1", limit: 2);
        Assert.Equal(2, all.Count);
        Assert.Equal("Third", all[0].Title);
        Assert.Equal("Second", all[1].Title);
    }

    [Fact]
    public async Task MarkReadAsync_FailsIfBelongsToDifferentUser()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("notif_security_test", organizationId: 1);
        var service = CreateService(ctx);

        await service.CreateAsync("user-1", AppNotificationType.ActivityRsvpConfirmed, "Private Notif");
        var list = await service.GetForUserAsync("user-1");
        var notifId = list[0].Id;

        // user-2 tries to mark user-1's notification as read
        var marked = await service.MarkReadAsync(notifId, "user-2");
        Assert.False(marked);

        // Verify still unread
        var refreshed = await service.GetForUserAsync("user-1");
        Assert.False(refreshed[0].IsRead);
    }

    [Fact]
    public async Task MarkAllReadAsync_MarksAllUnreadNotificationsForUser()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("notif_mark_all_test", organizationId: 1);
        var service = CreateService(ctx);

        await service.CreateAsync("user-1", AppNotificationType.ActivityRsvpConfirmed, "N1");
        await service.CreateAsync("user-1", AppNotificationType.ActivityWaitlisted, "N2");
        await service.CreateAsync("user-2", AppNotificationType.ActivityRsvpConfirmed, "N3");

        await service.MarkAllReadAsync("user-1");

        var count1 = await service.GetUnreadCountAsync("user-1");
        var count2 = await service.GetUnreadCountAsync("user-2");

        Assert.Equal(0, count1);
        Assert.Equal(1, count2); // user-2 still has 1 unread
    }

    [Fact]
    public async Task CreateBulkAsync_CreatesNotificationsForAllUserIds()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("notif_bulk_test", organizationId: 1);
        var service = CreateService(ctx);

        var users = new[] { "u1", "u2", "u3" };
        await service.CreateBulkAsync(users, AppNotificationType.ActivityCancelled, "Cancelled", "Activity is off");

        foreach (var u in users)
        {
            var notifs = await service.GetForUserAsync(u);
            Assert.Single(notifs);
            Assert.Equal(AppNotificationType.ActivityCancelled, notifs[0].Type);
            Assert.Equal("Cancelled", notifs[0].Title);
        }
    }

    [Theory]
    [InlineData(AppNotificationType.ActivityRsvpConfirmed, "bi-check-circle-fill")]
    [InlineData(AppNotificationType.ActivityWaitlisted, "bi-hourglass-split")]
    [InlineData(AppNotificationType.ActivityWaitlistPromoted, "bi-star-fill")]
    [InlineData(AppNotificationType.ActivityRsvpCancelled, "bi-x-circle-fill")]
    [InlineData(AppNotificationType.ActivityCancelled, "bi-exclamation-triangle-fill")]
    [InlineData(AppNotificationType.ActivityReminder, "bi-bell-fill")]
    [InlineData(AppNotificationType.ActivityNewRegistration, "bi-person-plus-fill")]
    [InlineData(AppNotificationType.ActivityRsvpCancelledAdmin, "bi-person-dash-fill")]
    public void GetIcon_ReturnsValidBootstrapIconClass(AppNotificationType type, string expectedClass)
    {
        var icon = AppNotificationService.GetIcon(type);
        Assert.Contains(expectedClass, icon);
    }
}
