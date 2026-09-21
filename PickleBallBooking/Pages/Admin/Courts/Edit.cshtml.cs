using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Courts;

[Authorize(Policy = TenantAdminAuthorization.TenantAdminPolicy)]
public class EditModel : PageModel
{
    private readonly ICourtService _courtService;
    private readonly ICourtImageStorage _imageStorage;

    public EditModel(ICourtService courtService, ICourtImageStorage imageStorage)
    {
        _courtService = courtService;
        _imageStorage = imageStorage;
    }

    [BindProperty]
    public int Id { get; set; }

    [BindProperty]
    public CourtInput Court { get; set; } = new();

    /// <summary>Public URL of the court's current image, or null if none.</summary>
    public string? CurrentImageUrl { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var court = await _courtService.GetByIdAsync(id);
        if (court is null)
        {
            return NotFound();
        }

        Id    = court.Id;
        Court = new CourtInput
        {
            Name        = court.Name,
            Description = court.Description,
        };
        CurrentImageUrl = _imageStorage.GetPublicUrl(court.ImagePath);

        return Page();
    }

    /// <summary>Saves court name/description and optionally replaces the image.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var updated = await _courtService.UpdateAsync(Id, Court.Name, Court.Description);
        if (!updated)
        {
            return NotFound();
        }

        // Reload to get the current stored image path.
        var court = await _courtService.GetByIdAsync(Id);
        if (court is null) return NotFound();

        if (Court.Image is not null && Court.Image.Length > 0)
        {
            try
            {
                // Upload new image first; only delete old one after the DB is updated.
                var newPath  = await _imageStorage.UploadAsync(
                    court.OrganizationId, court.Id, Court.Image);
                var oldPath  = court.ImagePath;

                await _courtService.SetImageAsync(Id, newPath);

                // Delete the old object if it existed and differs from the new path.
                if (!string.IsNullOrEmpty(oldPath) && oldPath != newPath)
                {
                    await _imageStorage.DeleteAsync(oldPath);
                }

                TempData["StatusMessage"] = "Court updated and image replaced.";
            }
            catch (CourtImageValidationException ex)
            {
                // Validation failed — court details were already saved. Show the error.
                TempData["StatusMessage"] = $"Court details saved, but image was not uploaded: {ex.Message}";
                return RedirectToPage(new { id = Id });
            }
            catch (Exception ex)
            {
                // Storage / network error — court details were saved, image was not.
                TempData["StatusMessage"] = $"Court details saved, but image upload failed: {ex.Message}";
                return RedirectToPage(new { id = Id });
            }
        }
        else
        {
            TempData["StatusMessage"] = "Court updated.";
        }

        return RedirectToPage("Index");
    }

    /// <summary>Removes the court's image (storage delete + DB clear).</summary>
    public async Task<IActionResult> OnPostRemoveImageAsync(int id)
    {
        // Load court through tenant-scoped filter — cross-tenant IDs return null.
        var court = await _courtService.GetByIdAsync(id);
        if (court is null) return NotFound();

        var existingPath = court.ImagePath;
        if (string.IsNullOrEmpty(existingPath))
        {
            TempData["StatusMessage"] = "No image to remove.";
            return RedirectToPage(new { id });
        }

        // Clear DB record first; then attempt storage deletion.
        await _courtService.RemoveImageAsync(id);

        try
        {
            await _imageStorage.DeleteAsync(existingPath);
        }
        catch
        {
            // Storage delete failed — DB is already clean. Log and continue.
            // The orphaned storage object can be cleaned up manually if needed.
        }

        TempData["StatusMessage"] = "Court image removed.";
        return RedirectToPage(new { id });
    }

    public class CourtInput
    {
        [Required]
        [MaxLength(100)]
        [Display(Name = "Court Name")]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        [Display(Name = "Description")]
        public string? Description { get; set; }

        /// <summary>Optional replacement image. If null/empty, existing image is kept.</summary>
        [Display(Name = "Replace Image")]
        public IFormFile? Image { get; set; }
    }
}
