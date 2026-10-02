using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Activities;

public class DetailModel : PageModel
{
    private readonly ActivityService _activityService;
    private readonly ActivityRsvpService _rsvpService;
    private readonly BookingTelegramService _telegram;
    private readonly IPlayerCheckInService _checkInService;
    private readonly ICourtAssignmentService _courtAssignmentService;

    private readonly ITenantPlayerService _playerService;
    private readonly ITenantContext _tenantContext;
    private readonly IRoundRobinService _roundRobinService;

    public DetailModel(
        ActivityService activityService,
        ActivityRsvpService rsvpService,
        BookingTelegramService telegram,
        IPlayerCheckInService checkInService,
        ICourtAssignmentService courtAssignmentService,
        IRoundRobinService roundRobinService,
        ITenantPlayerService playerService,
        ITenantContext tenantContext)
    {
        _activityService        = activityService;
        _rsvpService            = rsvpService;
        _telegram               = telegram;
        _checkInService         = checkInService;
        _courtAssignmentService = courtAssignmentService;
        _roundRobinService      = roundRobinService;
        _playerService          = playerService;
        _tenantContext          = tenantContext;
    }

    public Activity? Activity { get; private set; }
    public ActivityRosterResult? Roster { get; private set; }
    public ActivityCourtAssignmentOverviewDto? CourtOverview { get; private set; }
    public RoundRobinEventOverviewDto? RoundRobinOverview { get; private set; }
    public List<VenuePlayerListItemDto> AvailableVenuePlayers { get; private set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Activity = await _activityService.GetByIdAsync(id);
        if (Activity is null) return NotFound();

        Roster = await _rsvpService.GetRosterAsync(id);
        CourtOverview = await _courtAssignmentService.GetOverviewAsync(id);
        RoundRobinOverview = await _roundRobinService.GetEventOverviewAsync(id);

        var orgId = _tenantContext.OrganizationId;
        if (orgId.HasValue && Roster != null)
        {
            var existingUserIds = Roster.ConfirmedPlayers.Select(r => r.UserId)
                .Concat(Roster.WaitlistedPlayers.Select(w => w.UserId))
                .ToHashSet();

            var allPlayers = await _playerService.GetVenuePlayersAsync(orgId.Value);
            AvailableVenuePlayers = allPlayers.Where(p => !existingUserIds.Contains(p.UserId)).ToList();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSetStatusAsync(int id, ActivityStatus status)
    {
        // Phase 36: when an admin cancels an activity, bulk-notify all registered players.
        if (status == ActivityStatus.Cancelled)
        {
            var activity = await _activityService.GetByIdAsync(id);
            if (activity is not null)
            {
                await _rsvpService.NotifyActivityCancelledAsync(activity, _telegram);
            }
        }

        await _activityService.SetStatusAsync(id, status);
        StatusMessage = $"Activity status changed to {status}.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostPromoteAsync(int id, int rsvpId)
    {
        var (success, msg) = await _rsvpService.PromoteWaitlistedAsync(rsvpId);
        if (success) StatusMessage = msg;
        else ErrorMessage = msg;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveRsvpAsync(int id, string userId)
    {
        var result = await _rsvpService.CancelRsvpAsync(id, userId, isAdmin: true);
        if (result.Success)
        {
            StatusMessage = result.PromotedRsvp is not null
                ? "RSVP cancelled. Next waitlisted player was automatically promoted!"
                : "RSVP cancelled.";
        }
        else
        {
            ErrorMessage = result.Message;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostMoveWaitlistAsync(int id, int rsvpId, bool moveUp)
    {
        var (success, msg) = await _rsvpService.MoveWaitlistPositionAsync(rsvpId, moveUp);
        if (success) StatusMessage = msg;
        else ErrorMessage = msg;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCheckInRsvpAsync(int id, int rsvpId)
    {
        var staffUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "admin";
        var result = await _checkInService.CheckInRsvpAsync(rsvpId, CheckInMethod.AdminManual, staffUserId);
        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostMarkNoShowAsync(int id, int rsvpId)
    {
        var staffUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "admin";
        var result = await _checkInService.MarkRsvpNoShowAsync(rsvpId, staffUserId);
        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostUndoCheckInAsync(int id, int rsvpId)
    {
        var staffUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "admin";
        var result = await _checkInService.UndoRsvpCheckInAsync(rsvpId, staffUserId);
        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAddExistingPlayerAsync(int id, string userId, bool bypassCapacity)
    {
        var orgId = _tenantContext.OrganizationId;
        if (!orgId.HasValue) return Forbid();

        var result = await _playerService.AddPlayerToActivityAsync(orgId.Value, id, userId, bypassCapacity);
        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRegisterWalkInAndAddAsync(int id, CreateGuestPlayerDto input, bool bypassCapacity)
    {
        var orgId = _tenantContext.OrganizationId;
        if (!orgId.HasValue) return Forbid();

        if (string.IsNullOrWhiteSpace(input.FirstName) || string.IsNullOrWhiteSpace(input.LastName))
        {
            ErrorMessage = "First name and last name are required for walk-in player.";
            return RedirectToPage(new { id });
        }

        var result = await _playerService.RegisterAndAddToActivityAsync(orgId.Value, id, input, bypassCapacity);
        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await _activityService.DeleteAsync(id);
        return RedirectToPage("Index");
    }
}
