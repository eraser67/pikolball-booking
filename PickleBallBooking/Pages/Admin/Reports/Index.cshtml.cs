using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Reports;

[Authorize(Policy = TenantAdminAuthorization.TenantAdminPolicy)]
public class IndexModel : PageModel
{
    private readonly IReportService _reportService;
    private readonly ITenantContext _tenantContext;
    private readonly ApplicationDbContext _context;

    public IndexModel(
        IReportService reportService,
        ITenantContext tenantContext,
        ApplicationDbContext context)
    {
        _reportService = reportService;
        _tenantContext = tenantContext;
        _context = context;
    }

    [BindProperty(SupportsGet = true)]
    public int Year { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Month { get; set; }

    [BindProperty(SupportsGet = true)]
    public BookingStatus? StatusFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchQuery { get; set; }

    public MonthlyBookingReport Report { get; set; } = new();

    public int CurrentOrgId => _tenantContext.OrganizationId ?? _context.CurrentOrganizationId ?? 1;

    public async Task<IActionResult> OnGetAsync()
    {
        NormalizeYearMonth();

        Report = await _reportService.GetMonthlyReportAsync(CurrentOrgId, Year, Month);

        // Filter bookings ledger if filters are provided
        if (StatusFilter.HasValue)
        {
            Report.Bookings = Report.Bookings.Where(b => b.Status == StatusFilter.Value).ToList();
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var q = SearchQuery.Trim().ToLowerInvariant();
            Report.Bookings = Report.Bookings.Where(b =>
                b.BookingReference.ToLowerInvariant().Contains(q) ||
                b.CustomerName.ToLowerInvariant().Contains(q) ||
                b.CustomerPhone.ToLowerInvariant().Contains(q) ||
                (b.PaymentReference != null && b.PaymentReference.ToLowerInvariant().Contains(q)) ||
                b.CourtName.ToLowerInvariant().Contains(q)
            ).ToList();
        }

        return Page();
    }

    public async Task<IActionResult> OnGetExportCsvAsync()
    {
        NormalizeYearMonth();

        var csvBytes = await _reportService.ExportMonthlyReportCsvAsync(CurrentOrgId, Year, Month);
        var fileName = $"Monthly_Report_{Year}_{Month:D2}.csv";

        return File(csvBytes, "text/csv; charset=utf-8", fileName);
    }

    private void NormalizeYearMonth()
    {
        var now = DateTime.UtcNow;
        if (Year <= 0) Year = now.Year;
        if (Month <= 0 || Month > 12) Month = now.Month;
    }
}
