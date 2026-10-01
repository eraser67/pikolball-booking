using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using Xunit;

namespace PickleBallBooking.Tests;

public class ActivityRsvpServiceTests
{
    private sealed class EmailServiceSpy : IEmailService
    {
        public int SendCount { get; set; }
        public string? LastSubject { get; private set; }
        public string? LastToAddress { get; private set; }

        public Task SendAsync(
            string toAddress,
            string toName,
            string subject,
            string htmlBody,
            string? fromAddress = null,
            string? fromName = null,
            string? replyTo = null,
            CancellationToken ct = default)
        {
            SendCount++;
            LastToAddress = toAddress;
            LastSubject = subject;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCourtImageStorage : ICourtImageStorage
    {
        public Task<string> UploadAsync(int organizationId, int courtId, IFormFile file, CancellationToken ct = default) => Task.FromResult("path");
        public Task<string> UploadLogoAsync(int organizationId, IFormFile file, CancellationToken ct = default) => Task.FromResult("path");
        public Task<string> UploadHeroImageAsync(int organizationId, IFormFile file, CancellationToken ct = default) => Task.FromResult("path");
        public Task<string> UploadAvatarAsync(string userId, IFormFile file, CancellationToken ct = default) => Task.FromResult("path");
        public Task DeleteAsync(string storagePath, CancellationToken ct = default) => Task.CompletedTask;
        public string? GetPublicUrl(string? storagePath) => storagePath is not null ? $"https://storage.test/{storagePath}" : null;
    }

    private sealed class FakeTelegramService : ITelegramService
    {
        public int SendCount { get; set; }
        public string? LastChatId { get; private set; }
        public string? LastMessage { get; private set; }
        public Task<bool> SendMessageAsync(string chatId, string message, CancellationToken ct = default)
        {
            SendCount++;
            LastChatId = chatId;
            LastMessage = message;
            return Task.FromResult(true);
        }
        public string? DefaultChatId => "123456";
    }

    private static (ActivityRsvpService Service, EmailServiceSpy EmailSpy) CreateService(
        Data.ApplicationDbContext ctx)
    {
        var (service, spy, _) = CreateServiceWithTelegram(ctx);
        return (service, spy);
    }

    private static (ActivityRsvpService Service, EmailServiceSpy EmailSpy, FakeTelegramService TgSpy) CreateServiceWithTelegram(
        Data.ApplicationDbContext ctx)
    {
        var spy = new EmailServiceSpy();
        var emailOpts = Options.Create(new EmailOptions { Enabled = true, FromAddress = "noreply@punitbola.tech" });
        var emailService = new BookingEmailService(spy, NullLogger<BookingEmailService>.Instance, emailOpts);
        var notifService = new AppNotificationService(ctx, NullLogger<AppNotificationService>.Instance);
        var tgSpy = new FakeTelegramService();
        var tgOpts = Options.Create(new TelegramOptions { Enabled = true, BotToken = "123:ABC", DefaultChatId = "123456" });
        var tgService = new BookingTelegramService(tgSpy, tgOpts, NullLogger<BookingTelegramService>.Instance);
        var storage = new FakeCourtImageStorage();
        var service = new ActivityRsvpService(ctx, emailService, tgService, notifService, storage, NullLogger<ActivityRsvpService>.Instance);
        return (service, spy, tgSpy);
    }

    [Fact]
    public async Task JoinActivity_SpotsAvailable_ReturnsConfirmed()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("rsvp_spots_available", organizationId: 1);
        var (service, _) = CreateService(ctx);

        var activity = new Activity
        {
            OrganizationId = 1,
            Name = "Open Play Saturday",
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(2)),
            StartTime = TimeSpan.FromHours(8),
            EndTime = TimeSpan.FromHours(10),
            MaxCapacity = 2,
            Status = ActivityStatus.RegistrationOpen,
            PricePerPlayer = 0
        };
        ctx.Activities.Add(activity);
        await ctx.SaveChangesAsync();

        var result = await service.JoinActivityAsync(activity.Id, "user-1");

        Assert.True(result.Success);
        Assert.False(result.IsWaitlisted);
        Assert.NotNull(result.Rsvp);
        Assert.Equal(RsvpStatus.Confirmed, result.Rsvp.Status);
        Assert.Null(result.Rsvp.WaitlistPosition);
        Assert.Equal(1, result.Rsvp.OrganizationId);
        Assert.Equal(ActivityStatus.RegistrationOpen, activity.Status);

        // Second player joins -> fills capacity -> activity status changes to Full
        var result2 = await service.JoinActivityAsync(activity.Id, "user-2");
        Assert.True(result2.Success);
        Assert.False(result2.IsWaitlisted);
        Assert.Equal(RsvpStatus.Confirmed, result2.Rsvp!.Status);
        Assert.Equal(ActivityStatus.Full, activity.Status);
    }

    [Fact]
    public async Task JoinActivity_CapacityReached_ReturnsWaitlistedWithOrder()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("rsvp_waitlist_order", organizationId: 1);
        var (service, _) = CreateService(ctx);

        var activity = new Activity
        {
            OrganizationId = 1,
            Name = "Competitive Ladder",
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            StartTime = TimeSpan.FromHours(18),
            EndTime = TimeSpan.FromHours(20),
            MaxCapacity = 1,
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(activity);
        await ctx.SaveChangesAsync();

        // Player 1 fills spot
        await service.JoinActivityAsync(activity.Id, "user-1");

        // Player 2 waitlisted -> pos 1
        var result2 = await service.JoinActivityAsync(activity.Id, "user-2");
        Assert.True(result2.Success);
        Assert.True(result2.IsWaitlisted);
        Assert.Equal(RsvpStatus.Waitlisted, result2.Rsvp!.Status);
        Assert.Equal(1, result2.Rsvp.WaitlistPosition);

        // Player 3 waitlisted -> pos 2
        var result3 = await service.JoinActivityAsync(activity.Id, "user-3");
        Assert.True(result3.Success);
        Assert.True(result3.IsWaitlisted);
        Assert.Equal(RsvpStatus.Waitlisted, result3.Rsvp!.Status);
        Assert.Equal(2, result3.Rsvp.WaitlistPosition);
    }

    [Fact]
    public async Task JoinActivity_DuplicateAttempt_ReturnsError()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("rsvp_duplicate", organizationId: 1);
        var (service, _) = CreateService(ctx);

        var activity = new Activity
        {
            OrganizationId = 1,
            Name = "Sunday Social",
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(3)),
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(11),
            MaxCapacity = 5,
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(activity);
        await ctx.SaveChangesAsync();

        await service.JoinActivityAsync(activity.Id, "user-1");
        var dupResult = await service.JoinActivityAsync(activity.Id, "user-1");

        Assert.False(dupResult.Success);
        Assert.Contains("already secured a confirmed spot", dupResult.Message);
    }

    [Fact]
    public async Task CancelRsvp_ConfirmedPlayerWithWaitlist_PromotesFirstWaitlistedPlayer()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("rsvp_auto_promote", organizationId: 1);
        var (service, _) = CreateService(ctx);

        var activity = new Activity
        {
            OrganizationId = 1,
            Name = "Round Robin",
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(2)),
            StartTime = TimeSpan.FromHours(14),
            EndTime = TimeSpan.FromHours(16),
            MaxCapacity = 1,
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(activity);
        await ctx.SaveChangesAsync();

        await service.JoinActivityAsync(activity.Id, "user-1"); // Confirmed
        await service.JoinActivityAsync(activity.Id, "user-2"); // Waitlist #1
        await service.JoinActivityAsync(activity.Id, "user-3"); // Waitlist #2

        // User 1 cancels
        var cancelResult = await service.CancelRsvpAsync(activity.Id, "user-1");

        Assert.True(cancelResult.Success);
        Assert.NotNull(cancelResult.PromotedRsvp);
        Assert.Equal("user-2", cancelResult.PromotedRsvp.UserId);
        Assert.Equal(RsvpStatus.Confirmed, cancelResult.PromotedRsvp.Status);
        Assert.Null(cancelResult.PromotedRsvp.WaitlistPosition);

        // User 3 should now be Waitlist #1
        var user3Rsvp = await ctx.ActivityRsvps.FirstAsync(r => r.ActivityId == activity.Id && r.UserId == "user-3");
        Assert.Equal(RsvpStatus.Waitlisted, user3Rsvp.Status);
        Assert.Equal(1, user3Rsvp.WaitlistPosition);

        // Activity should still be Full because user-2 was promoted into the 1 available spot
        Assert.Equal(ActivityStatus.Full, activity.Status);
    }

    [Fact]
    public async Task CancelRsvp_ConfirmedPlayerNoWaitlist_ReopensRegistration()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("rsvp_reopen_status", organizationId: 1);
        var (service, _) = CreateService(ctx);

        var activity = new Activity
        {
            OrganizationId = 1,
            Name = "Small Drill",
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(2)),
            StartTime = TimeSpan.FromHours(10),
            EndTime = TimeSpan.FromHours(11),
            MaxCapacity = 1,
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(activity);
        await ctx.SaveChangesAsync();

        await service.JoinActivityAsync(activity.Id, "user-1");
        Assert.Equal(ActivityStatus.Full, activity.Status);

        // User 1 cancels and no one is on waitlist
        var cancelResult = await service.CancelRsvpAsync(activity.Id, "user-1");

        Assert.True(cancelResult.Success);
        Assert.Null(cancelResult.PromotedRsvp);
        Assert.Equal(ActivityStatus.RegistrationOpen, activity.Status);
    }

    [Fact]
    public async Task CancelRsvp_WaitlistedPlayer_ReindexesRemainingWaitlistWithoutGaps()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("rsvp_waitlist_reindex", organizationId: 1);
        var (service, _) = CreateService(ctx);

        var activity = new Activity
        {
            OrganizationId = 1,
            Name = "Clinic",
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            StartTime = TimeSpan.FromHours(8),
            EndTime = TimeSpan.FromHours(9),
            MaxCapacity = 1,
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(activity);
        await ctx.SaveChangesAsync();

        await service.JoinActivityAsync(activity.Id, "user-1"); // Confirmed
        await service.JoinActivityAsync(activity.Id, "user-2"); // Waitlist #1
        await service.JoinActivityAsync(activity.Id, "user-3"); // Waitlist #2
        await service.JoinActivityAsync(activity.Id, "user-4"); // Waitlist #3

        // User 3 (Waitlist #2) cancels
        var cancelResult = await service.CancelRsvpAsync(activity.Id, "user-3");
        Assert.True(cancelResult.Success);

        var rsvp2 = await ctx.ActivityRsvps.FirstAsync(r => r.ActivityId == activity.Id && r.UserId == "user-2");
        var rsvp4 = await ctx.ActivityRsvps.FirstAsync(r => r.ActivityId == activity.Id && r.UserId == "user-4");

        Assert.Equal(1, rsvp2.WaitlistPosition);
        Assert.Equal(2, rsvp4.WaitlistPosition); // shifted from 3 to 2
    }

    [Fact]
    public async Task PromoteWaitlisted_AdminAction_PromotesToConfirmed()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("rsvp_admin_promote", organizationId: 1);
        var (service, _) = CreateService(ctx);

        var activity = new Activity
        {
            OrganizationId = 1,
            Name = "Tournament Prep",
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(4)),
            StartTime = TimeSpan.FromHours(14),
            EndTime = TimeSpan.FromHours(16),
            MaxCapacity = 2,
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(activity);
        await ctx.SaveChangesAsync();

        await service.JoinActivityAsync(activity.Id, "user-1");
        await service.JoinActivityAsync(activity.Id, "user-2");
        var join3 = await service.JoinActivityAsync(activity.Id, "user-3"); // Waitlist #1

        // Admin promotes user 3
        var (success, _) = await service.PromoteWaitlistedAsync(join3.Rsvp!.Id);

        Assert.True(success);
        var promoted = await ctx.ActivityRsvps.FindAsync(join3.Rsvp.Id);
        Assert.Equal(RsvpStatus.Confirmed, promoted!.Status);
        Assert.Null(promoted.WaitlistPosition);
    }

    [Fact]
    public async Task MoveWaitlistPosition_SwapsPositionsCorrectly()
    {
        using var ctx = TestDbContextFactory.CreateInMemory("rsvp_move_waitlist", organizationId: 1);
        var (service, _) = CreateService(ctx);

        var activity = new Activity
        {
            OrganizationId = 1,
            Name = "Friendly Match",
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(5)),
            StartTime = TimeSpan.FromHours(15),
            EndTime = TimeSpan.FromHours(17),
            MaxCapacity = 1,
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(activity);
        await ctx.SaveChangesAsync();

        await service.JoinActivityAsync(activity.Id, "user-1");
        var join2 = await service.JoinActivityAsync(activity.Id, "user-2"); // #1
        var join3 = await service.JoinActivityAsync(activity.Id, "user-3"); // #2

        // Move User 3 Up -> should now be #1
        var (success, _) = await service.MoveWaitlistPositionAsync(join3.Rsvp!.Id, moveUp: true);
        Assert.True(success);

        var rsvp2 = await ctx.ActivityRsvps.FindAsync(join2.Rsvp!.Id);
        var rsvp3 = await ctx.ActivityRsvps.FindAsync(join3.Rsvp!.Id);

        Assert.Equal(1, rsvp3!.WaitlistPosition);
        Assert.Equal(2, rsvp2!.WaitlistPosition);
    }

    [Fact]
    public async Task TenantIsolation_RsvpsAreScopedToTenant()
    {
        // Setup Tenant 10 with an activity
        using (var ctx10 = TestDbContextFactory.CreateInMemory("rsvp_isolation_db", organizationId: 10))
        {
            var (service10, _) = CreateService(ctx10);
            var act10 = new Activity
            {
                OrganizationId = 10,
                Name = "Tenant 10 Open Play",
                Date = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
                StartTime = TimeSpan.FromHours(9),
                EndTime = TimeSpan.FromHours(11),
                MaxCapacity = 10,
                Status = ActivityStatus.RegistrationOpen
            };
            ctx10.Activities.Add(act10);
            await ctx10.SaveChangesAsync();

            var joinResult = await service10.JoinActivityAsync(act10.Id, "user-tenant-10");
            Assert.True(joinResult.Success);
            Assert.Equal(10, joinResult.Rsvp!.OrganizationId);
        }

        // Tenant 20 context cannot see Tenant 10's activity or RSVP
        using (var ctx20 = TestDbContextFactory.CreateInMemory("rsvp_isolation_db", organizationId: 20))
        {
            var (service20, _) = CreateService(ctx20);

            // Attempt to join Tenant 10's activity from Tenant 20 context -> Activity not found
            var result = await service20.JoinActivityAsync(1, "user-tenant-20");
            Assert.False(result.Success);
            Assert.Equal("Activity not found.", result.Message);

            // Cannot see RSVPs for Tenant 10
            var rsvps = await ctx20.ActivityRsvps.ToListAsync();
            Assert.Empty(rsvps);
        }
    }

    [Fact]
    public async Task GetUserActivities_CrossTenant_ReturnsAllUserActivities()
    {
        const string dbName = "rsvp_cross_tenant_user";

        // Seed Tenant 1 and 2
        using (var setupCtx = TestDbContextFactory.CreateInMemory(dbName, organizationId: 1))
        {
            setupCtx.Organizations.AddRange(
                new Organization { Id = 1, Name = "Makati Pickleball", Slug = "makati" },
                new Organization { Id = 2, Name = "BGC Pickleball", Slug = "bgc" }
            );
            await setupCtx.SaveChangesAsync();
        }

        // User registers in Org 1
        using (var ctx1 = TestDbContextFactory.CreateInMemory(dbName, organizationId: 1))
        {
            var (service1, _) = CreateService(ctx1);
            var act1 = new Activity
            {
                OrganizationId = 1,
                Name = "Makati Saturday Open",
                Date = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
                StartTime = TimeSpan.FromHours(8),
                EndTime = TimeSpan.FromHours(10),
                MaxCapacity = 10,
                Status = ActivityStatus.RegistrationOpen
            };
            ctx1.Activities.Add(act1);
            await ctx1.SaveChangesAsync();

            await service1.JoinActivityAsync(act1.Id, "cross-user");
        }

        // User registers in Org 2
        using (var ctx2 = TestDbContextFactory.CreateInMemory(dbName, organizationId: 2))
        {
            var (service2, _) = CreateService(ctx2);
            var act2 = new Activity
            {
                OrganizationId = 2,
                Name = "BGC Sunday League",
                Date = DateOnly.FromDateTime(DateTime.Today.AddDays(2)),
                StartTime = TimeSpan.FromHours(18),
                EndTime = TimeSpan.FromHours(20),
                MaxCapacity = 10,
                Status = ActivityStatus.RegistrationOpen
            };
            ctx2.Activities.Add(act2);
            await ctx2.SaveChangesAsync();

            await service2.JoinActivityAsync(act2.Id, "cross-user");
        }

        // Cross-tenant user dashboard lookup (can be executed from any context or without tenant)
        using (var dashCtx = TestDbContextFactory.CreateInMemory(dbName, organizationId: 1))
        {
            var (service, _) = CreateService(dashCtx);
            var userActs = await service.GetUserActivitiesAsync("cross-user");

            Assert.Equal(2, userActs.Count);
            Assert.Contains(userActs, a => a.ActivityName == "Makati Saturday Open" && a.OrganizationName == "Makati Pickleball");
            Assert.Contains(userActs, a => a.ActivityName == "BGC Sunday League" && a.OrganizationName == "BGC Pickleball");
        }
    }

    [Fact]
    public async Task JoinActivity_OrgAdminJoiningOwnOrgActivity_ReturnsErrorCannotJoin()
    {
        var dbName = Guid.NewGuid().ToString();
        using var ctx = TestDbContextFactory.CreateInMemory(dbName, organizationId: 1);

        // Set up organization membership for admin-user in Org 1
        ctx.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = 1,
            UserId = "admin-user",
            Role = OrganizationRole.OrganizationAdmin
        });

        var activity = new Activity
        {
            OrganizationId = 1,
            Name = "Admin Host Open Play",
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(11),
            MaxCapacity = 10,
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(activity);
        await ctx.SaveChangesAsync();

        var (service, _) = CreateService(ctx);

        var result = await service.JoinActivityAsync(activity.Id, "admin-user");

        Assert.False(result.Success);
        Assert.Equal("The admin for the current organization cannot join their own activities.", result.Message);

        var count = await ctx.ActivityRsvps.CountAsync(r => r.ActivityId == activity.Id);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task JoinActivity_OrgAdminJoiningOtherOrgActivity_Allowed()
    {
        var dbName = Guid.NewGuid().ToString();
        using var ctx = TestDbContextFactory.CreateInMemory(dbName, organizationId: 2);

        // User is admin of Org 1, NOT Org 2
        ctx.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = 1,
            UserId = "admin-user-org1",
            Role = OrganizationRole.OrganizationAdmin
        });

        var activity = new Activity
        {
            OrganizationId = 2,
            Name = "Other Org Play",
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(11),
            MaxCapacity = 10,
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(activity);
        await ctx.SaveChangesAsync();

        var (service, _) = CreateService(ctx);

        var result = await service.JoinActivityAsync(activity.Id, "admin-user-org1");

        Assert.True(result.Success);
        Assert.Equal(RsvpStatus.Confirmed, result.Rsvp?.Status);
    }

    [Fact]
    public async Task GetAdminActivityIdsAsync_ReturnsOnlyActivitiesUserAdministers()
    {
        var dbName = Guid.NewGuid().ToString();
        var act1 = new Activity
        {
            OrganizationId = 1,
            Name = "Org 1 Act",
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(11),
            Status = ActivityStatus.RegistrationOpen
        };

        using (var ctx1 = TestDbContextFactory.CreateInMemory(dbName, organizationId: 1))
        {
            ctx1.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = 1,
                UserId = "admin-1",
                Role = OrganizationRole.OrganizationOwner
            });
            ctx1.Activities.Add(act1);
            await ctx1.SaveChangesAsync();
        }

        var act2 = new Activity
        {
            OrganizationId = 2,
            Name = "Org 2 Act",
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(11),
            Status = ActivityStatus.RegistrationOpen
        };

        using (var ctx2 = TestDbContextFactory.CreateInMemory(dbName, organizationId: 2))
        {
            ctx2.Activities.Add(act2);
            await ctx2.SaveChangesAsync();
        }

        using (var ctx = TestDbContextFactory.CreateInMemory(dbName, organizationId: 1))
        {
            var (service, _) = CreateService(ctx);
            var adminIds = await service.GetAdminActivityIdsAsync(new[] { act1.Id, act2.Id }, "admin-1");

            Assert.Contains(act1.Id, adminIds);
            Assert.DoesNotContain(act2.Id, adminIds);
        }
    }

    [Fact]
    public async Task JoinActivity_CreatesInAppNotificationForPlayer()
    {
        var dbName = "rsvp_notif_join_test";
        using var ctx = TestDbContextFactory.CreateInMemory(dbName, organizationId: 1);
        var (service, _) = CreateService(ctx);

        var act = new Activity
        {
            OrganizationId = 1,
            Name = "Open Play Saturday",
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(11),
            MaxCapacity = 10,
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(act);
        await ctx.SaveChangesAsync();

        var res = await service.JoinActivityAsync(act.Id, "player-1");
        Assert.True(res.Success);

        var notifs = ctx.AppNotifications.Where(n => n.UserId == "player-1").ToList();
        Assert.Single(notifs);
        Assert.Equal(AppNotificationType.ActivityRsvpConfirmed, notifs[0].Type);
        Assert.Contains("Open Play Saturday", notifs[0].Title);
    }

    [Fact]
    public async Task CancelRsvp_WithWaitlist_CreatesNotificationsForBothPlayers()
    {
        var dbName = "rsvp_notif_cancel_promote_test";
        using var ctx = TestDbContextFactory.CreateInMemory(dbName, organizationId: 1);
        var (service, _) = CreateService(ctx);

        var act = new Activity
        {
            OrganizationId = 1,
            Name = "Competitive Ladder",
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            StartTime = TimeSpan.FromHours(10),
            EndTime = TimeSpan.FromHours(12),
            MaxCapacity = 1,
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(act);
        await ctx.SaveChangesAsync();

        await service.JoinActivityAsync(act.Id, "player-1");
        await service.JoinActivityAsync(act.Id, "player-2"); // goes to waitlist

        // Verify player-2 got waitlist notif
        var p2Notifs = ctx.AppNotifications.Where(n => n.UserId == "player-2").ToList();
        Assert.Single(p2Notifs);
        Assert.Equal(AppNotificationType.ActivityWaitlisted, p2Notifs[0].Type);

        // Player 1 cancels
        var cancelRes = await service.CancelRsvpAsync(act.Id, "player-1");
        Assert.True(cancelRes.Success);

        // Player 1 should have cancellation notif
        var p1Notifs = ctx.AppNotifications.Where(n => n.UserId == "player-1").ToList();
        Assert.Equal(2, p1Notifs.Count); // joined + cancelled
        Assert.Contains(p1Notifs, n => n.Type == AppNotificationType.ActivityRsvpCancelled);

        // Player 2 should now have promotion notif
        p2Notifs = ctx.AppNotifications.Where(n => n.UserId == "player-2").ToList();
        Assert.Equal(2, p2Notifs.Count); // waitlisted + promoted
        Assert.Contains(p2Notifs, n => n.Type == AppNotificationType.ActivityWaitlistPromoted);
    }

    [Fact]
    public async Task NotifyActivityCancelledAsync_BulkNotifiesConfirmedAndWaitlistedPlayers()
    {
        var dbName = "rsvp_notif_bulk_cancel_test";
        using var ctx = TestDbContextFactory.CreateInMemory(dbName, organizationId: 1);
        var (service, _) = CreateService(ctx);

        var act = new Activity
        {
            OrganizationId = 1,
            Name = "Cancelled Sunday Event",
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(2)),
            StartTime = TimeSpan.FromHours(8),
            EndTime = TimeSpan.FromHours(10),
            MaxCapacity = 1,
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(act);
        await ctx.SaveChangesAsync();

        await service.JoinActivityAsync(act.Id, "player-1");
        await service.JoinActivityAsync(act.Id, "player-2"); // waitlisted

        await service.NotifyActivityCancelledAsync(act);

        var p1CancelNotifs = ctx.AppNotifications.Where(n => n.UserId == "player-1" && n.Type == AppNotificationType.ActivityCancelled).ToList();
        var p2CancelNotifs = ctx.AppNotifications.Where(n => n.UserId == "player-2" && n.Type == AppNotificationType.ActivityCancelled).ToList();

        Assert.Single(p1CancelNotifs);
        Assert.Single(p2CancelNotifs);
        Assert.Contains("Cancelled Sunday Event", p1CancelNotifs[0].Title);
        Assert.Contains("Cancelled Sunday Event", p2CancelNotifs[0].Title);
    }

    [Fact]
    public async Task JoinActivity_AlertsAdminViaEmailTelegramAndInApp()
    {
        var dbName = "rsvp_admin_alert_test";
        using var ctx = TestDbContextFactory.CreateInMemory(dbName, organizationId: 1);
        var (service, emailSpy, tgSpy) = CreateServiceWithTelegram(ctx);

        // Setup Org with owner
        var org = new Organization
        {
            Id = 1,
            Name = "Pikolball",
            Slug = "pikolball",
            NotificationEmail = null // test fallback to owner email
        };
        ctx.Organizations.Add(org);

        var owner = new Microsoft.AspNetCore.Identity.IdentityUser
        {
            Id = "admin-user-1",
            Email = "owner@pikolball.com",
            UserName = "owner@pikolball.com"
        };
        ctx.Users.Add(owner);

        var player = new Microsoft.AspNetCore.Identity.IdentityUser
        {
            Id = "player-1",
            Email = "player1@example.com",
            UserName = "player1@example.com"
        };
        ctx.Users.Add(player);

        ctx.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = 1,
            UserId = "admin-user-1",
            Role = OrganizationRole.OrganizationOwner
        });

        var act = new Activity
        {
            OrganizationId = 1,
            Name = "Open Play Admin Alert",
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(11),
            MaxCapacity = 10,
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(act);
        await ctx.SaveChangesAsync();

        var res = await service.JoinActivityAsync(act.Id, "player-1");
        Assert.True(res.Success);

        // 1. Email sent to player + admin fallback
        Assert.True(emailSpy.SendCount >= 2);

        // 2. Telegram alert sent
        Assert.True(tgSpy.SendCount >= 1);
        Assert.Contains("New Activity Registration", tgSpy.LastMessage);

        // 3. In-App notification created for admin
        var adminNotifs = ctx.AppNotifications.Where(n => n.UserId == "admin-user-1").ToList();
        Assert.Single(adminNotifs);
        Assert.Equal(AppNotificationType.ActivityNewRegistration, adminNotifs[0].Type);
        Assert.Contains("Open Play Admin Alert", adminNotifs[0].Title);
    }

    [Fact]
    public async Task CancelRsvpAsync_WhenPlayerCancels_AlertsAdminAcrossChannels()
    {
        var dbName = "rsvp_admin_cancel_alert_test";
        using var ctx = TestDbContextFactory.CreateInMemory(dbName, organizationId: 1);
        var (service, emailSpy, tgSpy) = CreateServiceWithTelegram(ctx);

        var org = new Organization
        {
            Id = 1,
            Name = "Pikolball",
            Slug = "pikolball",
            NotificationEmail = null
        };
        ctx.Organizations.Add(org);

        var owner = new Microsoft.AspNetCore.Identity.IdentityUser
        {
            Id = "admin-user-1",
            Email = "owner@pikolball.com",
            UserName = "owner@pikolball.com"
        };
        ctx.Users.Add(owner);

        var player = new Microsoft.AspNetCore.Identity.IdentityUser
        {
            Id = "player-1",
            Email = "player1@example.com",
            UserName = "player1@example.com"
        };
        ctx.Users.Add(player);

        ctx.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = 1,
            UserId = "admin-user-1",
            Role = OrganizationRole.OrganizationOwner
        });

        var act = new Activity
        {
            OrganizationId = 1,
            Name = "Open Play Cancel Alert",
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(11),
            MaxCapacity = 10,
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(act);
        await ctx.SaveChangesAsync();

        // Player joins
        await service.JoinActivityAsync(act.Id, "player-1");

        // Clear spies & admin notifications from join
        emailSpy.SendCount = 0;
        tgSpy.SendCount = 0;
        var existingAdminNotifs = ctx.AppNotifications.Where(n => n.UserId == "admin-user-1").ToList();
        ctx.AppNotifications.RemoveRange(existingAdminNotifs);
        await ctx.SaveChangesAsync();

        // Player cancels RSVP
        var cancelRes = await service.CancelRsvpAsync(act.Id, "player-1", isAdmin: false);
        Assert.True(cancelRes.Success);

        // 1. Email sent: player cancellation email + admin cancellation alert
        Assert.True(emailSpy.SendCount >= 2);

        // 2. Telegram alert sent
        Assert.True(tgSpy.SendCount >= 1);
        Assert.Contains("Activity RSVP Cancelled", tgSpy.LastMessage);

        // 3. In-App notification created for admin
        var adminNotifs = ctx.AppNotifications.Where(n => n.UserId == "admin-user-1").ToList();
        Assert.Single(adminNotifs);
        Assert.Equal(AppNotificationType.ActivityRsvpCancelledAdmin, adminNotifs[0].Type);
        Assert.Contains("Open Play Cancel Alert", adminNotifs[0].Title);
        Assert.Contains("cancelled their registration", adminNotifs[0].Body);
    }

    [Fact]
    public async Task CancelRsvpAsync_WhenPlayerCancelsWithWaitlist_IncludesPromotedPlayerInAdminAlert()
    {
        var dbName = "rsvp_admin_cancel_promote_alert_test";
        using var ctx = TestDbContextFactory.CreateInMemory(dbName, organizationId: 1);
        var (service, emailSpy, tgSpy) = CreateServiceWithTelegram(ctx);

        var org = new Organization
        {
            Id = 1,
            Name = "Pikolball",
            Slug = "pikolball",
            NotificationEmail = "staff@pikolball.com"
        };
        ctx.Organizations.Add(org);

        var owner = new Microsoft.AspNetCore.Identity.IdentityUser
        {
            Id = "admin-user-1",
            Email = "staff@pikolball.com",
            UserName = "staff@pikolball.com"
        };
        ctx.Users.Add(owner);

        var p1 = new Microsoft.AspNetCore.Identity.IdentityUser
        {
            Id = "player-1",
            Email = "p1@example.com",
            UserName = "Alice"
        };
        var p2 = new Microsoft.AspNetCore.Identity.IdentityUser
        {
            Id = "player-2",
            Email = "p2@example.com",
            UserName = "Bob"
        };
        ctx.Users.AddRange(p1, p2);

        ctx.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = 1,
            UserId = "admin-user-1",
            Role = OrganizationRole.OrganizationAdmin
        });

        var act = new Activity
        {
            OrganizationId = 1,
            Name = "Singles Tourney",
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(2)),
            StartTime = TimeSpan.FromHours(14),
            EndTime = TimeSpan.FromHours(16),
            MaxCapacity = 1, // Only 1 spot
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(act);
        await ctx.SaveChangesAsync();

        await service.JoinActivityAsync(act.Id, "player-1"); // Confirmed
        await service.JoinActivityAsync(act.Id, "player-2"); // Waitlisted

        // Clear counts
        tgSpy.SendCount = 0;
        var existingAdminNotifs = ctx.AppNotifications.Where(n => n.UserId == "admin-user-1").ToList();
        ctx.AppNotifications.RemoveRange(existingAdminNotifs);
        await ctx.SaveChangesAsync();

        // Player 1 cancels -> Player 2 automatically promoted
        var cancelRes = await service.CancelRsvpAsync(act.Id, "player-1", isAdmin: false);
        Assert.True(cancelRes.Success);
        Assert.NotNull(cancelRes.PromotedRsvp);
        Assert.Equal("player-2", cancelRes.PromotedRsvp!.UserId);

        // Telegram alert should mention promoted player
        Assert.True(tgSpy.SendCount >= 1);
        Assert.Contains("Waitlist Auto-Promoted", tgSpy.LastMessage);
        Assert.Contains("Bob", tgSpy.LastMessage);

        // In-App notification should mention promoted player
        var adminNotif = ctx.AppNotifications.FirstOrDefault(n => n.UserId == "admin-user-1");
        Assert.NotNull(adminNotif);
        Assert.Equal(AppNotificationType.ActivityRsvpCancelledAdmin, adminNotif!.Type);
        Assert.Contains("Bob", adminNotif.Body);
        Assert.Contains("automatically promoted", adminNotif.Body);
    }

    [Fact]
    public async Task CancelRsvpAsync_WhenAdminCancels_DoesNotAlertAdmin()
    {
        var dbName = "rsvp_admin_cancel_self_test";
        using var ctx = TestDbContextFactory.CreateInMemory(dbName, organizationId: 1);
        var (service, emailSpy, tgSpy) = CreateServiceWithTelegram(ctx);

        var org = new Organization
        {
            Id = 1,
            Name = "Pikolball",
            Slug = "pikolball",
            NotificationEmail = "staff@pikolball.com"
        };
        ctx.Organizations.Add(org);

        var owner = new Microsoft.AspNetCore.Identity.IdentityUser
        {
            Id = "admin-user-1",
            Email = "staff@pikolball.com",
            UserName = "staff@pikolball.com"
        };
        ctx.Users.Add(owner);

        var p1 = new Microsoft.AspNetCore.Identity.IdentityUser
        {
            Id = "player-1",
            Email = "p1@example.com",
            UserName = "Alice"
        };
        ctx.Users.Add(p1);

        ctx.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = 1,
            UserId = "admin-user-1",
            Role = OrganizationRole.OrganizationAdmin
        });

        var act = new Activity
        {
            OrganizationId = 1,
            Name = "Open Session",
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(2)),
            StartTime = TimeSpan.FromHours(14),
            EndTime = TimeSpan.FromHours(16),
            MaxCapacity = 5,
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(act);
        await ctx.SaveChangesAsync();

        await service.JoinActivityAsync(act.Id, "player-1");

        // Clear
        tgSpy.SendCount = 0;
        var existingAdminNotifs = ctx.AppNotifications.Where(n => n.UserId == "admin-user-1").ToList();
        ctx.AppNotifications.RemoveRange(existingAdminNotifs);
        await ctx.SaveChangesAsync();

        // Admin cancels player RSVP (isAdmin: true)
        var cancelRes = await service.CancelRsvpAsync(act.Id, "player-1", isAdmin: true);
        Assert.True(cancelRes.Success);

        // Telegram alert should NOT be sent
        Assert.Equal(0, tgSpy.SendCount);

        // In-app admin alert should NOT be created
        var adminNotifs = ctx.AppNotifications.Where(n => n.UserId == "admin-user-1").ToList();
        Assert.Empty(adminNotifs);
    }
}

