using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Courts;

public class CreateModel : PageModel
{
    private readonly ICourtService _courtService;

    public CreateModel(ICourtService courtService)
    {
        _courtService = courtService;
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

        await _courtService.CreateAsync(Court.Name, Court.Description);

        return RedirectToPage("Index");
    }

    public class CourtInput
    {
        [Required]
        [MaxLength(100)]
        [Display(Name = "Name")]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        [Display(Name = "Description")]
        public string? Description { get; set; }
    }
}
