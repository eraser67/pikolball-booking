using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Courts;

[Authorize(Policy = TenantAdminAuthorization.TenantAdminPolicy)]
public class CreateModel : PageModel
{
    private readonly ICourtService _courtService;
    private readonly ICourtImageStorage _imageStorage;

    public CreateModel(ICourtService courtService, ICourtImageStorage imageStorage)
    {
        _courtService = courtService;
        _imageStorage = imageStorage;
    }

    [BindProperty]
    public CourtInput Court { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Create court (no image yet — keeps create atomic; image added afterwards).
        var court = await _courtService.CreateAsync(Court.Name, Court.Description);

        // Phase 24: optional image upload on creation.
        if (Court.Image is not null && Court.Image.Length > 0)
        {
            try
            {
                var path = await _imageStorage.UploadAsync(
                    court.OrganizationId, court.Id, Court.Image);
                await _courtService.SetImageAsync(court.Id, path);
            }
            catch (CourtImageValidationException ex)
            {
                // Image validation failed — court is already created, just no image.
                // Show the error but redirect successfully (user can add image from Edit).
                TempData["StatusMessage"] = $"Court created, but image was not saved: {ex.Message}";
                return RedirectToPage("Index");
            }
        }

        TempData["StatusMessage"] = $"Court '{court.Name}' created successfully.";
        return RedirectToPage("Index");
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

        /// <summary>Optional court image. Validated server-side for type and size.</summary>
        [Display(Name = "Court Image")]
        public IFormFile? Image { get; set; }
    }
}
