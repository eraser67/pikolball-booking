using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using Xunit;

namespace PickleBallBooking.Tests;

public class TenantPlayerServiceTests
{
    private sealed class InMemoryUserRoleStore :
        IUserStore<IdentityUser>,
        IUserEmailStore<IdentityUser>,
        IUserPasswordStore<IdentityUser>,
        IUserRoleStore<IdentityUser>
    {
        private readonly ApplicationDbContext _context;
        private readonly Dictionary<string, List<string>> _userRoles = new(StringComparer.OrdinalIgnoreCase);

        public InMemoryUserRoleStore(ApplicationDbContext context) => _context = context;

        public async Task<IdentityResult> CreateAsync(IdentityUser user, CancellationToken _)
        {
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return IdentityResult.Success;
        }

        public async Task<IdentityUser?> FindByEmailAsync(string normalizedEmail, CancellationToken _)
            => await _context.Users.FirstOrDefaultAsync(u =>
                u.NormalizedEmail == normalizedEmail || string.Equals(u.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase));

        public async Task<IdentityUser?> FindByIdAsync(string userId, CancellationToken _)
            => await _context.Users.FindAsync(userId);

        public Task<string> GetUserIdAsync(IdentityUser user, CancellationToken _) => Task.FromResult(user.Id);
        public Task<string?> GetUserNameAsync(IdentityUser user, CancellationToken _) => Task.FromResult(user.UserName);
        public Task SetUserNameAsync(IdentityUser user, string? n, CancellationToken _) { user.UserName = n; return Task.CompletedTask; }
        public Task<string?> GetNormalizedUserNameAsync(IdentityUser user, CancellationToken _) => Task.FromResult(user.NormalizedUserName);
        public Task SetNormalizedUserNameAsync(IdentityUser user, string? n, CancellationToken _) { user.NormalizedUserName = n; return Task.CompletedTask; }
        public Task<IdentityResult> UpdateAsync(IdentityUser user, CancellationToken _) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> DeleteAsync(IdentityUser user, CancellationToken _) => Task.FromResult(IdentityResult.Success);
        public async Task<IdentityUser?> FindByNameAsync(string normalizedUserName, CancellationToken _)
            => await _context.Users.FirstOrDefaultAsync(u => u.NormalizedUserName == normalizedUserName || u.UserName == normalizedUserName);

        public Task SetEmailAsync(IdentityUser u, string? e, CancellationToken _) { u.Email = e; return Task.CompletedTask; }
        public Task<string?> GetEmailAsync(IdentityUser u, CancellationToken _) => Task.FromResult(u.Email);
        public Task<bool> GetEmailConfirmedAsync(IdentityUser u, CancellationToken _) => Task.FromResult(u.EmailConfirmed);
        public Task SetEmailConfirmedAsync(IdentityUser u, bool c, CancellationToken _) { u.EmailConfirmed = c; return Task.CompletedTask; }
        public Task SetNormalizedEmailAsync(IdentityUser u, string? ne, CancellationToken _) { u.NormalizedEmail = ne; return Task.CompletedTask; }
        public Task<string?> GetNormalizedEmailAsync(IdentityUser u, CancellationToken _) => Task.FromResult(u.NormalizedEmail);

        public Task SetPasswordHashAsync(IdentityUser u, string? h, CancellationToken _) { u.PasswordHash = h; return Task.CompletedTask; }
        public Task<string?> GetPasswordHashAsync(IdentityUser u, CancellationToken _) => Task.FromResult(u.PasswordHash);
        public Task<bool> HasPasswordAsync(IdentityUser u, CancellationToken _) => Task.FromResult(u.PasswordHash != null);

        public Task AddToRoleAsync(IdentityUser user, string roleName, CancellationToken _)
        {
            if (!_userRoles.TryGetValue(user.Id, out var roles))
            {
                roles = new List<string>();
                _userRoles[user.Id] = roles;
            }
            if (!roles.Contains(roleName, StringComparer.OrdinalIgnoreCase))
                roles.Add(roleName);

            return Task.CompletedTask;
        }

        public Task RemoveFromRoleAsync(IdentityUser user, string roleName, CancellationToken _)
        {
            if (_userRoles.TryGetValue(user.Id, out var roles))
                roles.RemoveAll(r => string.Equals(r, roleName, StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        }

        public Task<IList<string>> GetRolesAsync(IdentityUser user, CancellationToken _)
        {
            IList<string> result = _userRoles.TryGetValue(user.Id, out var roles)
                ? roles.ToList()
                : new List<string>();
            return Task.FromResult(result);
        }

        public Task<bool> IsInRoleAsync(IdentityUser user, string roleName, CancellationToken _)
        {
            var inRole = _userRoles.TryGetValue(user.Id, out var roles) &&
                         roles.Contains(roleName, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(inRole);
        }

        public Task<IList<IdentityUser>> GetUsersInRoleAsync(string roleName, CancellationToken _)
        {
            var ids = _userRoles.Where(kv => kv.Value.Contains(roleName, StringComparer.OrdinalIgnoreCase)).Select(kv => kv.Key).ToHashSet();
            IList<IdentityUser> result = _context.Users.Where(u => ids.Contains(u.Id)).ToList();
            return Task.FromResult(result);
        }

        public void Dispose() { }
    }

    private static (TenantPlayerService Service, UserManager<IdentityUser> UserManager) CreateService(ApplicationDbContext ctx)
    {
        var store = new InMemoryUserRoleStore(ctx);
        var options = Options.Create(new IdentityOptions());
        var userManager = new UserManager<IdentityUser>(
            store,
            options,
            new PasswordHasher<IdentityUser>(),
            Array.Empty<IUserValidator<IdentityUser>>(),
            Array.Empty<IPasswordValidator<IdentityUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            new NullLogger<UserManager<IdentityUser>>());

        var service = new TenantPlayerService(ctx, userManager, NullLogger<TenantPlayerService>.Instance);
        return (service, userManager);
    }

    [Fact]
    public async Task CreateGuestPlayerAsync_ValidDto_CreatesIdentityUserAndPlayerProfileWithIsGuestTrue()
    {
        using var ctx = TestDbContextFactory.CreateInMemory(Guid.NewGuid().ToString(), organizationId: 1);
        var (service, userManager) = CreateService(ctx);

        var dto = new CreateGuestPlayerDto
        {
            FirstName = "John",
            LastName = "Doe",
            DisplayName = "Johnny D",
            SkillLevel = PlayerSkillLevel.Intermediate,
            PlayingHand = PlayingHand.Right,
            Mobile = "+639171234567",
            AdminNotes = "Prefers outdoor courts"
        };

        var profile = await service.CreateGuestPlayerAsync(1, dto);

        Assert.NotNull(profile);
        Assert.True(profile.IsGuest);
        Assert.Equal(1, profile.CreatedByOrganizationId);
        Assert.Equal("John", profile.FirstName);
        Assert.Equal("Doe", profile.LastName);
        Assert.Equal("Johnny D", profile.DisplayName);
        Assert.Equal(PlayerSkillLevel.Intermediate, profile.SkillLevel);
        Assert.Equal(PlayingHand.Right, profile.PlayingHand);
        Assert.Equal("+639171234567", profile.Mobile);
        Assert.Equal("Prefers outdoor courts", profile.AdminNotes);

        var user = await userManager.FindByIdAsync(profile.UserId);
        Assert.NotNull(user);
        Assert.StartsWith("guest_", user.UserName!);
        Assert.True(await userManager.IsInRoleAsync(user, "Customer"));
    }

    [Fact]
    public async Task CreateGuestPlayerAsync_MissingFirstOrLastName_ThrowsArgumentException()
    {
        using var ctx = TestDbContextFactory.CreateInMemory(Guid.NewGuid().ToString(), organizationId: 1);
        var (service, _) = CreateService(ctx);

        var dtoMissingFirst = new CreateGuestPlayerDto { FirstName = "", LastName = "Doe" };
        var dtoMissingLast = new CreateGuestPlayerDto { FirstName = "John", LastName = "   " };

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateGuestPlayerAsync(1, dtoMissingFirst));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateGuestPlayerAsync(1, dtoMissingLast));
    }

    [Fact]
    public async Task CreateGuestPlayerAsync_DuplicateEmail_GeneratesFallbackSyntheticEmail()
    {
        using var ctx = TestDbContextFactory.CreateInMemory(Guid.NewGuid().ToString(), organizationId: 1);
        var (service, userManager) = CreateService(ctx);

        // Pre-create an existing user with email "alice@example.com"
        var existing = new IdentityUser { Id = "existing-1", UserName = "alice", Email = "alice@example.com" };
        await userManager.CreateAsync(existing);

        var dto = new CreateGuestPlayerDto
        {
            FirstName = "Alice",
            LastName = "Guest",
            Email = "alice@example.com"
        };

        var profile = await service.CreateGuestPlayerAsync(1, dto);
        var guestUser = await userManager.FindByIdAsync(profile.UserId);

        Assert.NotNull(guestUser);
        Assert.NotEqual("alice@example.com", guestUser.Email);
        Assert.EndsWith("@guest.punitbola.tech", guestUser.Email!);
    }

    [Fact]
    public async Task UpdateGuestPlayerAsync_ValidChanges_UpdatesProfileFields()
    {
        using var ctx = TestDbContextFactory.CreateInMemory(Guid.NewGuid().ToString(), organizationId: 1);
        var (service, _) = CreateService(ctx);

        var created = await service.CreateGuestPlayerAsync(1, new CreateGuestPlayerDto
        {
            FirstName = "Mark",
            LastName = "Twain",
            SkillLevel = PlayerSkillLevel.Beginner
        });

        var updated = await service.UpdateGuestPlayerAsync(1, created.UserId, new UpdateGuestPlayerDto
        {
            FirstName = "Samuel",
            LastName = "Clemens",
            DisplayName = "Samuel Clemens",
            SkillLevel = PlayerSkillLevel.Advanced,
            PlayingHand = PlayingHand.Left,
            Mobile = "+639189876543",
            AdminNotes = "Promoted to Advanced"
        });

        Assert.True(updated);

        var refreshed = await service.GetGuestPlayerAsync(1, created.UserId);
        Assert.NotNull(refreshed);
        Assert.Equal("Samuel Clemens", refreshed.DisplayName);
        Assert.Equal(PlayerSkillLevel.Advanced, refreshed.SkillLevel);
        Assert.Equal(PlayingHand.Left, refreshed.PlayingHand);
        Assert.Equal("+639189876543", refreshed.Mobile);
        Assert.Equal("Promoted to Advanced", refreshed.AdminNotes);
    }

    [Fact]
    public async Task UpdateGuestPlayerAsync_BelongsToDifferentOrg_ReturnsFalse()
    {
        using var ctx = TestDbContextFactory.CreateInMemory(Guid.NewGuid().ToString(), organizationId: 1);
        var (service, _) = CreateService(ctx);

        var created = await service.CreateGuestPlayerAsync(1, new CreateGuestPlayerDto
        {
            FirstName = "Org1",
            LastName = "Player"
        });

        // Org 2 tries to update Org 1's guest player
        var result = await service.UpdateGuestPlayerAsync(2, created.UserId, new UpdateGuestPlayerDto
        {
            DisplayName = "Hacked Name"
        });

        Assert.False(result);
    }

    [Fact]
    public async Task GetVenuePlayersAsync_ReturnsGuestsAndRsvpdMembers_WithFiltering()
    {
        using var ctx = TestDbContextFactory.CreateInMemory(Guid.NewGuid().ToString(), organizationId: 1);
        var (service, userManager) = CreateService(ctx);

        // 1. Create a guest player created by Org 1
        var guest = await service.CreateGuestPlayerAsync(1, new CreateGuestPlayerDto
        {
            FirstName = "WalkIn",
            LastName = "Player",
            DisplayName = "WalkIn Star"
        });

        // 2. Create a registered member who has RSVP'd to an activity at Org 1
        var memberUser = new IdentityUser { Id = "member-user-1", UserName = "regmember", Email = "member@test.com" };
        await userManager.CreateAsync(memberUser);

        var memberProfile = new PlayerProfile
        {
            UserId = memberUser.Id,
            FirstName = "Registered",
            LastName = "Member",
            DisplayName = "Reggie",
            IsGuest = false
        };
        ctx.PlayerProfiles.Add(memberProfile);

        var activity = new Activity
        {
            Id = 10,
            OrganizationId = 1,
            Name = "Club Night",
            Date = DateOnly.FromDateTime(DateTime.Today),
            StartTime = TimeSpan.FromHours(18),
            EndTime = TimeSpan.FromHours(20),
            MaxCapacity = 10,
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(activity);

        ctx.ActivityRsvps.Add(new ActivityRsvp
        {
            OrganizationId = 1,
            ActivityId = activity.Id,
            UserId = memberUser.Id,
            Status = RsvpStatus.Confirmed
        });
        await ctx.SaveChangesAsync();

        // Query all venue players
        var allPlayers = await service.GetVenuePlayersAsync(1);
        Assert.Equal(2, allPlayers.Count);

        // Filter only guests
        var onlyGuests = await service.GetVenuePlayersAsync(1, onlyGuests: true);
        Assert.Single(onlyGuests);
        Assert.True(onlyGuests[0].IsGuest);

        // Filter only registered members
        var onlyMembers = await service.GetVenuePlayersAsync(1, onlyGuests: false);
        Assert.Single(onlyMembers);
        Assert.False(onlyMembers[0].IsGuest);

        // Search by name
        var searchWalk = await service.GetVenuePlayersAsync(1, search: "WalkIn");
        Assert.Single(searchWalk);
        Assert.Equal("WalkIn Star", searchWalk[0].DisplayName);
    }

    [Fact]
    public async Task AddPlayerToActivityAsync_UnderCapacity_ConfirmsRsvp()
    {
        using var ctx = TestDbContextFactory.CreateInMemory(Guid.NewGuid().ToString(), organizationId: 1);
        var (service, _) = CreateService(ctx);

        var guest = await service.CreateGuestPlayerAsync(1, new CreateGuestPlayerDto
        {
            FirstName = "Ben",
            LastName = "Johns"
        });

        var activity = new Activity
        {
            Id = 20,
            OrganizationId = 1,
            Name = "Singles Tourney",
            Date = DateOnly.FromDateTime(DateTime.Today),
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(12),
            MaxCapacity = 4,
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(activity);
        await ctx.SaveChangesAsync();

        var result = await service.AddPlayerToActivityAsync(1, activity.Id, guest.UserId);

        Assert.True(result.Success);
        Assert.Equal(RsvpStatus.Confirmed, result.Status);

        var rsvp = await ctx.ActivityRsvps.FirstOrDefaultAsync(r => r.ActivityId == activity.Id && r.UserId == guest.UserId);
        Assert.NotNull(rsvp);
        Assert.Equal(RsvpStatus.Confirmed, rsvp.Status);
    }

    [Fact]
    public async Task AddPlayerToActivityAsync_AtCapacity_WaitlistsByDefault_ConfirmsIfBypassCapacityTrue()
    {
        using var ctx = TestDbContextFactory.CreateInMemory(Guid.NewGuid().ToString(), organizationId: 1);
        var (service, userManager) = CreateService(ctx);

        var activity = new Activity
        {
            Id = 30,
            OrganizationId = 1,
            Name = "Full Clinic",
            Date = DateOnly.FromDateTime(DateTime.Today),
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(11),
            MaxCapacity = 1,
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(activity);

        // Fill capacity with 1 confirmed RSVP
        var existingUser = new IdentityUser { Id = "user-cap-1", UserName = "cap1" };
        await userManager.CreateAsync(existingUser);
        ctx.ActivityRsvps.Add(new ActivityRsvp
        {
            OrganizationId = 1,
            ActivityId = activity.Id,
            UserId = existingUser.Id,
            Status = RsvpStatus.Confirmed
        });
        await ctx.SaveChangesAsync();

        var guest1 = await service.CreateGuestPlayerAsync(1, new CreateGuestPlayerDto { FirstName = "Guest", LastName = "One" });
        var guest2 = await service.CreateGuestPlayerAsync(1, new CreateGuestPlayerDto { FirstName = "Guest", LastName = "Two" });

        // Without bypass -> waitlisted
        var resultWaitlist = await service.AddPlayerToActivityAsync(1, activity.Id, guest1.UserId, bypassCapacity: false);
        Assert.True(resultWaitlist.Success);
        Assert.Equal(RsvpStatus.Waitlisted, resultWaitlist.Status);

        // With bypass -> confirmed
        var resultBypass = await service.AddPlayerToActivityAsync(1, activity.Id, guest2.UserId, bypassCapacity: true);
        Assert.True(resultBypass.Success);
        Assert.Equal(RsvpStatus.Confirmed, resultBypass.Status);
    }

    [Fact]
    public async Task RegisterAndAddToActivityAsync_AtomicallyRegistersAndRsvps()
    {
        using var ctx = TestDbContextFactory.CreateInMemory(Guid.NewGuid().ToString(), organizationId: 1);
        var (service, _) = CreateService(ctx);

        var activity = new Activity
        {
            Id = 40,
            OrganizationId = 1,
            Name = "Open Play",
            Date = DateOnly.FromDateTime(DateTime.Today),
            StartTime = TimeSpan.FromHours(14),
            EndTime = TimeSpan.FromHours(16),
            MaxCapacity = 10,
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(activity);
        await ctx.SaveChangesAsync();

        var guestDto = new CreateGuestPlayerDto
        {
            FirstName = "WalkIn",
            LastName = "Rsvper",
            DisplayName = "Fast WalkIn",
            SkillLevel = PlayerSkillLevel.Intermediate,
            Mobile = "+639999999999"
        };

        var result = await service.RegisterAndAddToActivityAsync(1, activity.Id, guestDto);

        Assert.True(result.Success);
        Assert.Equal(RsvpStatus.Confirmed, result.Status);
        Assert.NotNull(result.RsvpId);

        var rsvp = await ctx.ActivityRsvps.FirstOrDefaultAsync(r => r.Id == result.RsvpId);
        Assert.NotNull(rsvp);
        Assert.Equal(RsvpStatus.Confirmed, rsvp.Status);

        var profile = await ctx.PlayerProfiles.FirstOrDefaultAsync(p => p.UserId == rsvp.UserId);
        Assert.NotNull(profile);
        Assert.True(profile.IsGuest);
        Assert.Equal("Fast WalkIn", profile.DisplayName);
    }

    [Fact]
    public async Task GuestPlayer_FullFlow_CheckIn_CourtAssignment_MatchScoring_Statistics()
    {
        using var ctx = TestDbContextFactory.CreateInMemory(Guid.NewGuid().ToString(), organizationId: 1);
        var (service, userManager) = CreateService(ctx);

        // 1. Create a guest player and a regular player
        var guest = await service.CreateGuestPlayerAsync(1, new CreateGuestPlayerDto
        {
            FirstName = "Guest",
            LastName = "Champion",
            DisplayName = "The Guest Pro",
            SkillLevel = PlayerSkillLevel.Advanced
        });

        var regUser = new IdentityUser { Id = "reg-user-1", UserName = "regchamp", Email = "reg@test.com" };
        await userManager.CreateAsync(regUser);
        ctx.PlayerProfiles.Add(new PlayerProfile
        {
            UserId = regUser.Id,
            FirstName = "Regular",
            LastName = "Opponent",
            DisplayName = "Reg Opponent",
            SkillLevel = PlayerSkillLevel.Advanced,
            IsGuest = false
        });

        // 2. Create activity and courts
        var activity = new Activity
        {
            Id = 50,
            OrganizationId = 1,
            Name = "Championship Finals",
            Date = DateOnly.FromDateTime(DateTime.Today),
            StartTime = TimeSpan.FromHours(10),
            EndTime = TimeSpan.FromHours(12),
            MaxCapacity = 8,
            Status = ActivityStatus.RegistrationOpen
        };
        ctx.Activities.Add(activity);

        var court = new Court
        {
            Id = 1,
            OrganizationId = 1,
            Name = "Center Court",
            Status = CourtStatus.Active
        };
        ctx.Courts.Add(court);

        var ac = new ActivityCourt { OrganizationId = 1, ActivityId = activity.Id, CourtId = court.Id };
        ctx.ActivityCourts.Add(ac);

        await ctx.SaveChangesAsync();

        var guestAddResult = await service.AddPlayerToActivityAsync(1, activity.Id, guest.UserId);
        var regAddResult = await service.AddPlayerToActivityAsync(1, activity.Id, regUser.Id);
        Assert.True(guestAddResult.Success);
        Assert.True(regAddResult.Success);

        // 3. Check-In via PlayerCheckInService
        var fakeStorage = new FakeCourtImageStorage();
        var notifService = new AppNotificationService(ctx, NullLogger<AppNotificationService>.Instance);
        var checkInService = new PlayerCheckInService(ctx, notifService, fakeStorage, NullLogger<PlayerCheckInService>.Instance);

        var guestRsvp = await ctx.ActivityRsvps.FirstAsync(r => r.ActivityId == activity.Id && r.UserId == guest.UserId);
        var checkInResult = await checkInService.CheckInRsvpAsync(guestRsvp.Id, CheckInMethod.AdminManual, "staff-user-1");
        Assert.True(checkInResult.Success);
        Assert.Equal(RsvpStatus.CheckedIn, checkInResult.RsvpStatus);

        // 4. Assign Court via CourtAssignmentService
        var courtAssignService = new CourtAssignmentService(ctx, fakeStorage, notifService);
        var assignResult = await courtAssignService.AssignPlayerAsync(
            activity.Id,
            guestRsvp.Id,
            court.Id,
            slotNumber: 1,
            adminUserId: "staff-user-1");
        Assert.True(assignResult.Success);

        // 5. Match Scoring via MatchScoringService
        var tenantCtx = new TenantContext { OrganizationId = 1 };
        var matchScoringService = new MatchScoringService(ctx, tenantCtx, notifService, NullLogger<MatchScoringService>.Instance);

        var rrEvent = new RoundRobinEvent
        {
            Id = 1,
            OrganizationId = 1,
            ActivityId = activity.Id,
            Format = RoundRobinFormat.RotatingPartners,
            NumberOfRounds = 1,
            PointsToWin = 11,
            WinByTwo = true,
            IsLocked = true
        };
        ctx.RoundRobinEvents.Add(rrEvent);

        var match = new RoundRobinMatch
        {
            Id = 101,
            OrganizationId = 1,
            RoundRobinEventId = rrEvent.Id,
            RoundNumber = 1,
            CourtId = court.Id,
            CourtName = court.Name,
            Team1Player1UserId = guest.UserId,
            Team1Player1Name = "The Guest Pro",
            Team2Player1UserId = regUser.Id,
            Team2Player1Name = "Reg Opponent",
            Status = MatchStatus.InProgress
        };
        ctx.RoundRobinMatches.Add(match);
        await ctx.SaveChangesAsync();

        var scoreResult = await matchScoringService.RecordScoreAsync(
            match.Id,
            new SubmitScoreDto(11, 7, null, IsLiveUpdate: false),
            "admin-user");

        Assert.True(scoreResult.Success);

        // Finalize match to make it official for stats
        var finResult = await matchScoringService.FinalizeMatchAsync(match.Id, "admin-user");
        Assert.True(finResult.Success);

        // 6. Verify PlayerStatisticsService reflects guest stats and IsGuest == true
        var statsService = new PlayerStatisticsService(ctx, fakeStorage, NullLogger<PlayerStatisticsService>.Instance);
        var profileDto = await statsService.GetPlayerStatisticsAsync(guest.UserId, viewerUserId: null, isStaffOrAdmin: true);

        Assert.NotNull(profileDto);
        Assert.True(profileDto.IsGuest);
        Assert.Equal("The Guest Pro", profileDto.DisplayName);
        Assert.Equal(1, profileDto.Overall.MatchesPlayed);
        Assert.Equal(1, profileDto.Overall.Wins);
        Assert.Equal(100.0, profileDto.Overall.WinPercentage);
    }

    private sealed class FakeCourtImageStorage : ICourtImageStorage
    {
        public Task<string> UploadAsync(int organizationId, int courtId, Microsoft.AspNetCore.Http.IFormFile file, CancellationToken ct = default) => Task.FromResult("path");
        public Task<string> UploadLogoAsync(int organizationId, Microsoft.AspNetCore.Http.IFormFile file, CancellationToken ct = default) => Task.FromResult("path");
        public Task<string> UploadHeroImageAsync(int organizationId, Microsoft.AspNetCore.Http.IFormFile file, CancellationToken ct = default) => Task.FromResult("path");
        public Task<string> UploadAvatarAsync(string userId, Microsoft.AspNetCore.Http.IFormFile file, CancellationToken ct = default) => Task.FromResult("path");
        public Task DeleteAsync(string storagePath, CancellationToken ct = default) => Task.CompletedTask;
        public string? GetPublicUrl(string? storagePath) => storagePath != null ? $"https://storage.test/{storagePath}" : null;
    }
}
