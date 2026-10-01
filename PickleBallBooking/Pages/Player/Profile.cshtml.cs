using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Player;

public class ProfileModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IPlayerStatisticsService _statisticsService;
    private readonly ITenantContext _tenantContext;

    public ProfileModel(
        UserManager<IdentityUser> userManager,
        IPlayerStatisticsService statisticsService,
        ITenantContext tenantContext)
    {
        _userManager = userManager;
        _statisticsService = statisticsService;
        _tenantContext = tenantContext;
    }

    public PlayerStatisticsProfileDto? PlayerStats { get; private set; }
    public bool IsSelf { get; private set; }
    public bool IsAdmin { get; private set; }
    public string? CurrentUserId { get; private set; }
    public int? CurrentOrgId => _tenantContext.OrganizationId;

    [BindProperty(SupportsGet = true)]
    public string? Filter { get; set; } // "all" or "tenant"

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(string? userId)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        CurrentUserId = currentUser?.Id;
        IsAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

        // If no userId provided, default to current logged-in user
        var targetUserId = string.IsNullOrWhiteSpace(userId) ? CurrentUserId : userId;

        if (string.IsNullOrWhiteSpace(targetUserId))
        {
            // Unauthenticated visitor tried /Player without specifying a user
            return RedirectToPage("/Account/Login");
        }

        IsSelf = !string.IsNullOrEmpty(CurrentUserId) &&
                 string.Equals(CurrentUserId, targetUserId, StringComparison.OrdinalIgnoreCase);

        int? orgScope = (Filter == "tenant") ? CurrentOrgId : null;

        PlayerStats = await _statisticsService.GetPlayerStatisticsAsync(
            targetUserId: targetUserId,
            viewerUserId: CurrentUserId,
            isStaffOrAdmin: IsAdmin,
            organizationId: orgScope);

        if (PlayerStats == null)
        {
            return NotFound("Player profile not found.");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostUpdatePrivacyAsync(string targetUserId, MatchHistoryPrivacyLevel privacy)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser == null) return Challenge();

        bool isOwner = string.Equals(currentUser.Id, targetUserId, StringComparison.OrdinalIgnoreCase);
        bool isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

        if (!isOwner && !isAdmin)
        {
            return Forbid();
        }

        var success = await _statisticsService.UpdateMatchHistoryPrivacyAsync(targetUserId, privacy);
        if (success)
        {
            StatusMessage = "Match history privacy updated successfully.";
        }

        return RedirectToPage("/Player/Profile", new { userId = targetUserId });
    }
}
