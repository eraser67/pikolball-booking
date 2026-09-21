using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Courts;

[Authorize(Policy = TenantAdminAuthorization.TenantAdminPolicy)]
public class IndexModel : PageModel
{
    private readonly ICourtService _courtService;
    private readonly ICourtImageStorage _imageStorage;

    public IndexModel(ICourtService courtService, ICourtImageStorage imageStorage)
    {
        _courtService  = courtService;
        _imageStorage  = imageStorage;
    }

    public List<Court> Courts { get; set; } = new();

    /// <summary>Map from court ID to public image URL (null when no image).</summary>
    public Dictionary<int, string?> CourtImageUrls { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        Courts = await _courtService.GetAllAsync();
        foreach (var court in Courts)
        {
            CourtImageUrls[court.Id] = _imageStorage.GetPublicUrl(court.ImagePath);
        }
    }

    public async Task<IActionResult> OnPostToggleStatusAsync(int id)
    {
        var court = await _courtService.GetByIdAsync(id);
        if (court is null)
        {
            return NotFound();
        }

        var newStatus = court.Status == CourtStatus.Active ? CourtStatus.Inactive : CourtStatus.Active;
        await _courtService.SetStatusAsync(id, newStatus);

        StatusMessage = $"Court '{court.Name}' has been {(newStatus == CourtStatus.Active ? "activated" : "deactivated")}.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var court = await _courtService.GetByIdAsync(id);
        if (court is null)
        {
            return NotFound();
        }

        try
        {
            await _courtService.DeleteAsync(id);
            StatusMessage = $"Court '{court.Name}' has been deleted.";
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
        }

        return RedirectToPage();
    }
}
