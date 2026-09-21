using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Payments;

[Authorize(Policy = TenantAdminAuthorization.TenantAdminPolicy)]
public class IndexModel : PageModel
{
    private readonly IPaymentService _paymentService;

    public IndexModel(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public List<PaymentSummary> Payments { get; set; } = new();

    public PaymentStatus? StatusFilter { get; set; }

    /// <summary>Search term for client-side filtering by booking reference or customer name.</summary>
    public string? SearchQuery { get; set; }

    public async Task OnGetAsync(PaymentStatus? status = null, string? q = null)
    {
        StatusFilter  = status;
        SearchQuery   = q?.Trim();
        Payments      = await _paymentService.GetAllForTenantAsync(status);
    }
}
