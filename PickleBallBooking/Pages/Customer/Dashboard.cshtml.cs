using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Customer;

/// <summary>
/// Phase 34: customer / player dashboard.
/// Displays player profile, recent court reservations, and registered activities (RSVPs and waitlists).
/// </summary>
public class DashboardModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly PlayerProfileService _profileService;
    private readonly ActivityRsvpService _rsvpService;
    private readonly ICourtImageStorage _storage;

    public DashboardModel(
        UserManager<IdentityUser> userManager,
        ApplicationDbContext context,
        PlayerProfileService profileService,
        ActivityRsvpService rsvpService,
        ICourtImageStorage storage)
    {
        _userManager    = userManager;
        _context        = context;
        _profileService = profileService;
        _rsvpService    = rsvpService;
        _storage        = storage;
    }

    public string DisplayName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string Mobile { get; private set; } = string.Empty;
    public string? Location { get; private set; }
    public string? SkillLevelLabel { get; private set; }
    public string? AvatarUrl { get; private set; }
    public bool HasProfile { get; private set; }

    public IReadOnlyList<BookingSummary> RecentBookings { get; private set; } = [];
    public IReadOnlyList<UserActivityItem> UserActivities { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return RedirectToPage("/Account/Login");

        Email  = user.Email ?? string.Empty;
        Mobile = user.PhoneNumber ?? string.Empty;

        var profile = await _profileService.GetByUserIdAsync(user.Id);
        HasProfile  = profile is not null;
        AvatarUrl   = _storage.GetPublicUrl(profile?.AvatarPath);

        if (profile is not null)
        {
            DisplayName     = profile.DisplayName.Length > 0 ? profile.DisplayName : $"{profile.FirstName} {profile.LastName}";
            Mobile          = profile.Mobile ?? Mobile;
            Location        = profile.Location;
            SkillLevelLabel = profile.SkillLevel switch
            {
                PlayerSkillLevel.Beginner     => "🟢 Beginner",
                PlayerSkillLevel.Intermediate => "🟡 Intermediate",
                PlayerSkillLevel.Advanced     => "🔴 Advanced",
                _                             => null
            };
        }
        else
        {
            DisplayName = User.FindFirstValue("fullName") ?? Email;
        }

        RecentBookings = await _context.Bookings
            .IgnoreQueryFilters()
            .Include(b => b.Court)
            .Where(b => b.CustomerEmail == Email)
            .OrderByDescending(b => b.CreatedAt)
            .Take(5)
            .Select(b => new BookingSummary
            {
                Id        = b.Id,
                CourtName = b.Court != null ? b.Court.Name : "Court",
                Date      = b.BookingDate,
                StartTime = b.StartTime,
                EndTime   = b.EndTime,
                Status    = b.BookingStatus,
                Price     = b.Price,
                Reference = b.BookingReference,
                CreatedAt = b.CreatedAt
            })
            .ToListAsync();

        UserActivities = await _rsvpService.GetUserActivitiesAsync(user.Id);

        return Page();
    }

    public async Task<IActionResult> OnPostCancelRsvpAsync(int activityId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return RedirectToPage("/Account/Login");

        var result = await _rsvpService.CancelRsvpAsync(activityId, user.Id);
        if (result.Success)
        {
            StatusMessage = result.Message;
        }
        else
        {
            ErrorMessage = result.Message;
        }

        return RedirectToPage();
    }

    public record BookingSummary
    {
        public int Id { get; init; }
        public string CourtName { get; init; } = string.Empty;
        public DateOnly Date { get; init; }
        public TimeSpan StartTime { get; init; }
        public TimeSpan EndTime { get; init; }
        public BookingStatus Status { get; init; }
        public decimal Price { get; init; }
        public string Reference { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
    }
}
