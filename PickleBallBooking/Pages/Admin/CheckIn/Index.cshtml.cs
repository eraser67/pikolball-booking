using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.CheckIn;

/// <summary>
/// Phase 38: Venue staff check-in dashboard.
/// Supports QR scanning, manual check-in, no-show recording, and real-time attendance queue.
/// </summary>
[Authorize(Policy = "TenantAdmin")]
public class IndexModel : PageModel
{
    private readonly IPlayerCheckInService _checkInService;
    private readonly ITenantContext _tenantContext;

    public IndexModel(IPlayerCheckInService checkInService, ITenantContext tenantContext)
    {
        _checkInService = checkInService;
        _tenantContext  = tenantContext;
    }

    public TodayCheckInFeed Feed { get; private set; } = null!;
    public DateOnly SelectedDate { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(DateOnly? date = null)
    {
        SelectedDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        var orgId = _tenantContext.OrganizationId;

        Feed = await _checkInService.GetTodayCheckInFeedAsync(orgId, SelectedDate);
        return Page();
    }

    public async Task<IActionResult> OnPostProcessQrAsync(string qrPayload, DateOnly? date = null)
    {
        var orgId = _tenantContext.OrganizationId;
        var staffUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";

        var result = await _checkInService.ProcessQrCodeAsync(qrPayload, staffUserId, orgId);
        return HandleResult(result, date);
    }

    public async Task<IActionResult> OnPostCheckInRsvpAsync(int rsvpId, DateOnly? date = null)
    {
        var staffUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var result = await _checkInService.CheckInRsvpAsync(rsvpId, CheckInMethod.AdminManual, staffUserId);
        return HandleResult(result, date);
    }

    public async Task<IActionResult> OnPostMarkRsvpNoShowAsync(int rsvpId, DateOnly? date = null)
    {
        var staffUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var result = await _checkInService.MarkRsvpNoShowAsync(rsvpId, staffUserId);
        return HandleResult(result, date);
    }

    public async Task<IActionResult> OnPostUndoRsvpAsync(int rsvpId, DateOnly? date = null)
    {
        var staffUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var result = await _checkInService.UndoRsvpCheckInAsync(rsvpId, staffUserId);
        return HandleResult(result, date);
    }

    public async Task<IActionResult> OnPostCheckInBookingAsync(string bookingReference, DateOnly? date = null)
    {
        var staffUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var result = await _checkInService.CheckInBookingAsync(bookingReference, CheckInMethod.AdminManual, staffUserId);
        return HandleResult(result, date);
    }

    public async Task<IActionResult> OnPostMarkBookingNoShowAsync(string bookingReference, DateOnly? date = null)
    {
        var staffUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var result = await _checkInService.MarkBookingNoShowAsync(bookingReference, staffUserId);
        return HandleResult(result, date);
    }

    public async Task<IActionResult> OnPostUndoBookingAsync(string bookingReference, DateOnly? date = null)
    {
        var staffUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var result = await _checkInService.UndoBookingCheckInAsync(bookingReference, staffUserId);
        return HandleResult(result, date);
    }

    private IActionResult HandleResult(CheckInResult result, DateOnly? date)
    {
        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
            Request.Headers["Accept"].ToString().Contains("application/json"))
        {
            return new JsonResult(result);
        }

        if (result.Success) StatusMessage = result.Message;
        else ErrorMessage = result.Message;

        return RedirectToPage(new { date = date?.ToString("yyyy-MM-dd") });
    }
}
