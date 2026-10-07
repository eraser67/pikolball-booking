using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.PaymentOptions;

[Authorize(Policy = TenantAdminAuthorization.TenantAdminPolicy)]
public class IndexModel : PageModel
{
    private readonly IPaymentService _paymentService;
    private readonly IPaymentProofStorage _proofStorage;

    public IndexModel(IPaymentService paymentService, IPaymentProofStorage proofStorage)
    {
        _paymentService = paymentService;
        _proofStorage   = proofStorage;
    }

    public List<TenantPaymentOption> Options { get; set; } = new();

    /// <summary>Resolved public QR URLs keyed by option Id.</summary>
    public Dictionary<int, string?> QRUrls { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        Options = await _paymentService.GetAllPaymentOptionsAsync();
        foreach (var opt in Options)
        {
            QRUrls[opt.Id] = _proofStorage.GetQRCodePublicUrl(opt.QRCodeImagePath);
        }
    }

    /// <summary>Toggle active/inactive for a payment option.</summary>
    public async Task<IActionResult> OnPostToggleAsync(int id, bool active)
    {
        var option = await _paymentService.GetPaymentOptionByIdAsync(id);
        if (option is null) return NotFound();

        await _paymentService.UpdatePaymentOptionAsync(
            id,
            option.Label,
            option.AccountName,
            option.AccountNumber,
            option.Instructions,
            null, // keep existing QR
            isActive: active,
            option.DisplayOrder);

        StatusMessage = active ? $"'{option.Label}' enabled." : $"'{option.Label}' disabled.";
        return RedirectToPage();
    }

    /// <summary>Delete a payment option.</summary>
    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var option = await _paymentService.GetPaymentOptionByIdAsync(id);
        var label = option?.Label ?? "Option";
        var ok = await _paymentService.DeletePaymentOptionAsync(id);
        StatusMessage = ok ? $"'{label}' deleted." : "Option not found.";
        return RedirectToPage();
    }
}
