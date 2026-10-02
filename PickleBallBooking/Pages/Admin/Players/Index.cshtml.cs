using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Players;

[Authorize(Policy = TenantAdminAuthorization.TenantAdminPolicy)]
public class IndexModel : PageModel
{
    private readonly ITenantPlayerService _playerService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        ITenantPlayerService playerService,
        ITenantContext tenantContext,
        ILogger<IndexModel> logger)
    {
        _playerService = playerService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public List<VenuePlayerListItemDto> Players { get; private set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Filter { get; set; } = "all";

    public int TotalCount { get; private set; }
    public int GuestCount { get; private set; }
    public int RegisteredCount { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var orgId = _tenantContext.OrganizationId;
        if (!orgId.HasValue) return Forbid();

        var allPlayers = await _playerService.GetVenuePlayersAsync(orgId.Value, search: null, onlyGuests: null);
        TotalCount = allPlayers.Count;
        GuestCount = allPlayers.Count(p => p.IsGuest);
        RegisteredCount = allPlayers.Count(p => !p.IsGuest);

        bool? onlyGuestsFilter = Filter switch
        {
            "guests" => true,
            "registered" => false,
            _ => null
        };

        Players = await _playerService.GetVenuePlayersAsync(orgId.Value, Search, onlyGuestsFilter);

        return Page();
    }

    public async Task<IActionResult> OnPostRegisterGuestAsync(CreateGuestPlayerDto input)
    {
        var orgId = _tenantContext.OrganizationId;
        if (!orgId.HasValue) return Forbid();

        if (string.IsNullOrWhiteSpace(input.FirstName) || string.IsNullOrWhiteSpace(input.LastName))
        {
            ErrorMessage = "First name and last name are required.";
            return RedirectToPage(new { Search, Filter });
        }

        try
        {
            var profile = await _playerService.CreateGuestPlayerAsync(orgId.Value, input);
            StatusMessage = $"Walk-in player \"{profile.DisplayName}\" was successfully registered!";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering walk-in player for org {OrgId}", orgId.Value);
            ErrorMessage = $"Failed to register player: {ex.Message}";
        }

        return RedirectToPage(new { Search, Filter });
    }

    public async Task<IActionResult> OnPostUpdateGuestAsync(string userId, UpdateGuestPlayerDto input)
    {
        var orgId = _tenantContext.OrganizationId;
        if (!orgId.HasValue) return Forbid();

        if (string.IsNullOrWhiteSpace(input.FirstName) || string.IsNullOrWhiteSpace(input.LastName))
        {
            ErrorMessage = "First name and last name are required.";
            return RedirectToPage(new { Search, Filter });
        }

        try
        {
            var success = await _playerService.UpdateGuestPlayerAsync(orgId.Value, userId, input);
            if (success)
            {
                StatusMessage = $"Player \"{input.DisplayName ?? input.FirstName}\" was updated successfully.";
            }
            else
            {
                ErrorMessage = "Player not found or cannot be edited by this organization.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating guest player {UserId} for org {OrgId}", userId, orgId.Value);
            ErrorMessage = $"Failed to update player: {ex.Message}";
        }

        return RedirectToPage(new { Search, Filter });
    }
}
