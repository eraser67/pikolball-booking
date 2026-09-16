using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Courts;

public class EditModel : PageModel
{
    private readonly ICourtService _courtService;

    public EditModel(ICourtService courtService)
    {
        _courtService = courtService;
    }

    [BindProperty]
    public int Id { get; set; }

    [BindProperty]
    public CourtInput Court { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var court = await _courtService.GetByIdAsync(id);
        if (court is null)
        {
            return NotFound();
        }

        Id = court.Id;
        Court = new CourtInput
        {
            Name = court.Name,
            Description = court.Description
        };

        return Page();
    }

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
