using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public class TenantPlayerService : ITenantPlayerService
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ILogger<TenantPlayerService> _logger;

    public TenantPlayerService(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        ILogger<TenantPlayerService> logger)
    {
        _context = context;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<PlayerProfile> CreateGuestPlayerAsync(int orgId, CreateGuestPlayerDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.FirstName))
            throw new ArgumentException("First name is required.", nameof(dto.FirstName));
        if (string.IsNullOrWhiteSpace(dto.LastName))
            throw new ArgumentException("Last name is required.", nameof(dto.LastName));

        var uniqueKey = Guid.NewGuid().ToString("N")[..12];
        var userName = $"guest_{uniqueKey}";
        var email = !string.IsNullOrWhiteSpace(dto.Email)
            ? dto.Email.Trim().ToLowerInvariant()
            : $"{userName}@guest.punitbola.tech";

        // Check if email is already taken by an active user
        var existingUser = await _userManager.FindByEmailAsync(email);
        if (existingUser != null)
        {
            email = $"guest_{Guid.NewGuid():N}@guest.punitbola.tech";
        }

        var identityUser = new IdentityUser
        {
            UserName = userName,
            Email = email,
            PhoneNumber = dto.Mobile?.Trim(),
            EmailConfirmed = false
        };

        // Create with an unguessable 64-char password hash
        var randomSecret = $"G!{Guid.NewGuid():N}!{Guid.NewGuid():N}#A9";
        var createResult = await _userManager.CreateAsync(identityUser, randomSecret);
        if (!createResult.Succeeded)
        {
            var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
            _logger.LogError("Failed to create IdentityUser for guest player: {Errors}", errors);
            throw new InvalidOperationException($"Could not register guest user: {errors}");
        }

        // Add Customer role
        await _userManager.AddToRoleAsync(identityUser, "Customer");

        var displayName = !string.IsNullOrWhiteSpace(dto.DisplayName)
            ? dto.DisplayName.Trim()
            : $"{dto.FirstName.Trim()} {dto.LastName.Trim()}".Trim();

        var profile = new PlayerProfile
        {
            UserId = identityUser.Id,
            FirstName = dto.FirstName.Trim(),
            LastName = dto.LastName.Trim(),
            DisplayName = displayName,
            SkillLevel = dto.SkillLevel,
            PlayingHand = dto.PlayingHand,
            Mobile = dto.Mobile?.Trim(),
            IsGuest = true,
            CreatedByOrganizationId = orgId,
            AdminNotes = dto.AdminNotes?.Trim(),
            IsDiscoverable = false, // Private to venue by default
            PrivacyMatchHistory = MatchHistoryPrivacyLevel.Public,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.PlayerProfiles.Add(profile);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created tenant-managed guest player {DisplayName} (Id: {ProfileId}, UserId: {UserId}) for Org {OrgId}",
            displayName, profile.Id, identityUser.Id, orgId);

        return profile;
    }

    public async Task<List<VenuePlayerListItemDto>> GetVenuePlayersAsync(int orgId, string? search = null, bool? onlyGuests = null)
    {
        // 1. Gather all relevant UserIds
        var guestUserIds = await _context.PlayerProfiles
            .Where(p => p.IsGuest && p.CreatedByOrganizationId == orgId)
            .Select(p => p.UserId)
            .ToListAsync();

        List<string> userIds;
        if (onlyGuests == true)
        {
            userIds = guestUserIds;
        }
        else
        {
            var activityUserIds = await _context.ActivityRsvps
                .Where(r => r.OrganizationId == orgId)
                .Select(r => r.UserId)
                .Distinct()
                .ToListAsync();

            if (onlyGuests == false)
            {
                var guestSet = guestUserIds.ToHashSet();
                userIds = activityUserIds.Where(id => !guestSet.Contains(id)).Distinct().ToList();
            }
            else
            {
                userIds = guestUserIds
                    .Union(activityUserIds)
                    .Distinct()
                    .ToList();
            }
        }

        if (userIds.Count == 0)
            return new List<VenuePlayerListItemDto>();

        // 2. Load Profiles
        var profiles = await _context.PlayerProfiles
            .Where(p => userIds.Contains(p.UserId))
            .ToListAsync();

        var profileMap = profiles.ToDictionary(p => p.UserId);

        // 3. Load Identity Users for fallbacks
        var users = await _context.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        // 4. Calculate participation metrics scoped to this organization
        var attendanceStats = await _context.ActivityRsvps
            .Where(r => r.OrganizationId == orgId && userIds.Contains(r.UserId) &&
                        (r.Status == RsvpStatus.Confirmed || r.Status == RsvpStatus.CheckedIn))
            .GroupBy(r => r.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                Count = g.Count(),
                LastActive = g.Max(r => r.CreatedAt)
            })
            .ToDictionaryAsync(x => x.UserId);

        // Matches played in finalized events for this org
        var matchesStats = await _context.RoundRobinMatches
            .Where(m => m.OrganizationId == orgId && m.IsFinalized &&
                        (userIds.Contains(m.Team1Player1UserId!) ||
                         userIds.Contains(m.Team1Player2UserId!) ||
                         userIds.Contains(m.Team2Player1UserId!) ||
                         userIds.Contains(m.Team2Player2UserId!)))
            .Select(m => new
            {
                m.Team1Player1UserId,
                m.Team1Player2UserId,
                m.Team2Player1UserId,
                m.Team2Player2UserId,
                m.FinalizedAt
            })
            .ToListAsync();

        var matchCounts = new Dictionary<string, int>();
        var matchLastActive = new Dictionary<string, DateTime?>();
        foreach (var m in matchesStats)
        {
            var pIds = new[] { m.Team1Player1UserId, m.Team1Player2UserId, m.Team2Player1UserId, m.Team2Player2UserId }
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct();

            foreach (var pid in pIds)
            {
                if (string.IsNullOrEmpty(pid)) continue;
                matchCounts[pid] = matchCounts.GetValueOrDefault(pid) + 1;
                if (m.FinalizedAt.HasValue)
                {
                    if (!matchLastActive.ContainsKey(pid) || m.FinalizedAt > matchLastActive[pid])
                        matchLastActive[pid] = m.FinalizedAt;
                }
            }
        }

        // 5. Build DTO list
        var results = new List<VenuePlayerListItemDto>();

        foreach (var uid in userIds)
        {
            profileMap.TryGetValue(uid, out var profile);
            users.TryGetValue(uid, out var user);

            var isGuest = profile?.IsGuest ?? false;
            var displayName = profile?.DisplayName ??
                              (profile != null ? $"{profile.FirstName} {profile.LastName}".Trim() : null) ??
                              user?.UserName ?? "Player";

            var fullName = profile != null ? $"{profile.FirstName} {profile.LastName}".Trim() : displayName;
            var skill = profile?.SkillLevel ?? PlayerSkillLevel.Beginner;
            var hand = profile?.PlayingHand ?? PlayingHand.Right;
            var mobile = profile?.Mobile ?? user?.PhoneNumber;
            var email = !string.IsNullOrEmpty(user?.Email) && !user.Email.EndsWith("@guest.punitbola.tech")
                ? user.Email
                : null;

            var attended = attendanceStats.TryGetValue(uid, out var aStat) ? aStat.Count : 0;
            var played = matchCounts.GetValueOrDefault(uid, 0);

            DateTime? lastActive = aStat?.LastActive;
            if (matchLastActive.TryGetValue(uid, out var mDate) && mDate.HasValue)
            {
                if (!lastActive.HasValue || mDate > lastActive)
                    lastActive = mDate;
            }

            var item = new VenuePlayerListItemDto
            {
                UserId = uid,
                PlayerProfileId = profile?.Id ?? 0,
                DisplayName = displayName,
                FullName = fullName,
                SkillLevel = skill,
                PlayingHand = hand,
                Mobile = mobile,
                Email = email,
                IsGuest = isGuest,
                CreatedByOrganizationId = profile?.CreatedByOrganizationId,
                AdminNotes = profile?.AdminNotes,
                ActivitiesAttended = attended,
                MatchesPlayed = played,
                LastActiveDate = lastActive,
                CreatedAt = profile?.CreatedAt ?? DateTime.UtcNow
            };

            results.Add(item);
        }

        if (onlyGuests.HasValue)
        {
            results = results.Where(p => p.IsGuest == onlyGuests.Value).ToList();
        }

        // Apply search filter if provided
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            results = results.Where(p =>
                p.DisplayName.ToLowerInvariant().Contains(term) ||
                p.FullName.ToLowerInvariant().Contains(term) ||
                (p.Mobile != null && p.Mobile.Contains(term)) ||
                (p.Email != null && p.Email.ToLowerInvariant().Contains(term)) ||
                (p.AdminNotes != null && p.AdminNotes.ToLowerInvariant().Contains(term))
            ).ToList();
        }

        return results
            .OrderByDescending(p => p.IsGuest)
            .ThenByDescending(p => p.LastActiveDate ?? p.CreatedAt)
            .ToList();
    }

    public async Task<PlayerProfile?> GetGuestPlayerAsync(int orgId, string userId)
    {
        return await _context.PlayerProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId && p.IsGuest && p.CreatedByOrganizationId == orgId);
    }

    public async Task<bool> UpdateGuestPlayerAsync(int orgId, string userId, UpdateGuestPlayerDto dto)
    {
        var profile = await GetGuestPlayerAsync(orgId, userId);
        if (profile == null) return false;

        profile.FirstName = dto.FirstName.Trim();
        profile.LastName = dto.LastName.Trim();
        profile.DisplayName = !string.IsNullOrWhiteSpace(dto.DisplayName)
            ? dto.DisplayName.Trim()
            : $"{profile.FirstName} {profile.LastName}".Trim();
        profile.SkillLevel = dto.SkillLevel;
        profile.PlayingHand = dto.PlayingHand;
        profile.Mobile = dto.Mobile?.Trim();
        profile.AdminNotes = dto.AdminNotes?.Trim();
        profile.UpdatedAt = DateTime.UtcNow;

        var user = await _userManager.FindByIdAsync(userId);
        if (user != null)
        {
            user.PhoneNumber = profile.Mobile;
            if (!string.IsNullOrWhiteSpace(dto.Email) && !dto.Email.EndsWith("@guest.punitbola.tech"))
            {
                user.Email = dto.Email.Trim().ToLowerInvariant();
            }
            await _userManager.UpdateAsync(user);
        }

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<AddPlayerToActivityResult> AddPlayerToActivityAsync(
        int orgId,
        int activityId,
        string userId,
        bool bypassCapacity = false)
    {
        var activity = await _context.Activities
            .Include(a => a.Rsvps)
            .FirstOrDefaultAsync(a => a.Id == activityId && a.OrganizationId == orgId);

        if (activity == null)
        {
            return new AddPlayerToActivityResult { Success = false, Message = "Activity not found." };
        }

        if (activity.Status == ActivityStatus.Cancelled || activity.Status == ActivityStatus.Completed)
        {
            return new AddPlayerToActivityResult
            {
                Success = false,
                Message = $"Cannot add players to an activity with status '{activity.Status}'."
            };
        }

        // Check if player is already RSVP'd
        var existing = activity.Rsvps.FirstOrDefault(r => r.UserId == userId && r.Status != RsvpStatus.Cancelled);
        if (existing != null)
        {
            return new AddPlayerToActivityResult
            {
                Success = false,
                Message = $"Player is already on the roster with status '{existing.Status}'."
            };
        }

        var confirmedCount = activity.Rsvps.Count(r => r.Status == RsvpStatus.Confirmed || r.Status == RsvpStatus.CheckedIn);
        var isFull = activity.MaxCapacity > 0 && confirmedCount >= activity.MaxCapacity;

        if (isFull && !bypassCapacity)
        {
            // Add to waitlist
            var maxPos = activity.Rsvps
                .Where(r => r.Status == RsvpStatus.Waitlisted && r.WaitlistPosition.HasValue)
                .Select(r => r.WaitlistPosition!.Value)
                .DefaultIfEmpty(0)
                .Max();

            var nextPos = maxPos + 1;
            var waitlistRsvp = new ActivityRsvp
            {
                OrganizationId = orgId,
                ActivityId = activityId,
                UserId = userId,
                Status = RsvpStatus.Waitlisted,
                WaitlistPosition = nextPos,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.ActivityRsvps.Add(waitlistRsvp);
            await _context.SaveChangesAsync();

            return new AddPlayerToActivityResult
            {
                Success = true,
                Message = $"Activity is full. Player added to waitlist at position #{nextPos}.",
                RsvpId = waitlistRsvp.Id,
                Status = RsvpStatus.Waitlisted,
                WaitlistPosition = nextPos
            };
        }

        // Add as Confirmed
        var confirmedRsvp = new ActivityRsvp
        {
            OrganizationId = orgId,
            ActivityId = activityId,
            UserId = userId,
            Status = RsvpStatus.Confirmed,
            WaitlistPosition = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.ActivityRsvps.Add(confirmedRsvp);
        await _context.SaveChangesAsync();

        return new AddPlayerToActivityResult
        {
            Success = true,
            Message = "Player successfully added to activity as Confirmed.",
            RsvpId = confirmedRsvp.Id,
            Status = RsvpStatus.Confirmed
        };
    }

    public async Task<AddPlayerToActivityResult> RegisterAndAddToActivityAsync(
        int orgId,
        int activityId,
        CreateGuestPlayerDto dto,
        bool bypassCapacity = false)
    {
        var profile = await CreateGuestPlayerAsync(orgId, dto);
        return await AddPlayerToActivityAsync(orgId, activityId, profile.UserId, bypassCapacity);
    }
}
