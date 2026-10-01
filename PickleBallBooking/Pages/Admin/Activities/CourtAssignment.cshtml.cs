using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Activities;

public class CourtAssignmentModel : PageModel
{
    private readonly ICourtAssignmentService _courtAssignmentService;

    public CourtAssignmentModel(ICourtAssignmentService courtAssignmentService)
    {
        _courtAssignmentService = courtAssignmentService;
    }

    public ActivityCourtAssignmentOverviewDto? Overview { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Overview = await _courtAssignmentService.GetOverviewAsync(id);
        if (Overview is null) return NotFound();

        return Page();
    }

    public async Task<IActionResult> OnPostAssignAsync(int id, int rsvpId, int courtId, int? slotNumber)
    {
        var adminUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "admin";
        var result = await _courtAssignmentService.AssignPlayerAsync(id, rsvpId, courtId, slotNumber, adminUserId);

        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostMoveAsync(int id, int rsvpId, int targetCourtId)
    {
        var adminUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "admin";
        var result = await _courtAssignmentService.MovePlayerAsync(id, rsvpId, targetCourtId, null, adminUserId);

        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostUnassignAsync(int id, int rsvpId)
    {
        var adminUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "admin";
        var result = await _courtAssignmentService.UnassignPlayerAsync(id, rsvpId, adminUserId);

        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAutoAssignAsync(int id)
    {
        var adminUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "admin";
        var result = await _courtAssignmentService.AutoAssignAsync(id, adminUserId);

        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSkillGroupAsync(int id)
    {
        var adminUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "admin";
        var result = await _courtAssignmentService.SkillBasedGroupAsync(id, adminUserId);

        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRebalanceAsync(int id)
    {
        var adminUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "admin";
        var result = await _courtAssignmentService.RebalanceAsync(id, adminUserId);

        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostClearAsync(int id)
    {
        var adminUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "admin";
        var result = await _courtAssignmentService.ClearAssignmentsAsync(id, adminUserId);

        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostToggleLockAsync(int id, bool lockAssignments)
    {
        var adminUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "admin";
        var result = await _courtAssignmentService.SetLockAsync(id, lockAssignments, adminUserId);

        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { id });
    }
}
