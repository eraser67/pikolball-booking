using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Data;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Reports;

[Authorize(Policy = TenantAdminAuthorization.TenantAdminPolicy)]
public class CustomersModel : PageModel
{
    private readonly IReportService _reportService;
    private readonly ITenantContext _tenantContext;
    private readonly ApplicationDbContext _context;

    public CustomersModel(
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
    public string? SearchQuery { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? TierFilter { get; set; }

    public CustomerInsightsReport Report { get; set; } = new();

    public int CurrentOrgId => _tenantContext.OrganizationId ?? _context.CurrentOrganizationId ?? 1;

    public async Task<IActionResult> OnGetAsync()
    {
        NormalizeYearMonth();
        Report = await _reportService.GetCustomerInsightsReportAsync(CurrentOrgId, Year, Month);

        if (!string.IsNullOrWhiteSpace(TierFilter) && TierFilter != "all")
        {
            switch (TierFilter.ToLowerInvariant())
            {
                case "vip":
                    Report.TopPlayers = Report.TopPlayers.Where(p => p.TotalBookings >= 5).ToList();
                    break;
                case "regular":
                    Report.TopPlayers = Report.TopPlayers.Where(p => p.TotalBookings >= 2 && p.TotalBookings < 5).ToList();
                    break;
                case "occasional":
                    Report.TopPlayers = Report.TopPlayers.Where(p => p.TotalBookings == 1).ToList();
                    break;
                case "new":
                    Report.TopPlayers = Report.TopPlayers.Where(p => p.IsNewCustomerThisMonth).ToList();
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var q = SearchQuery.Trim().ToLowerInvariant();
            Report.TopPlayers = Report.TopPlayers.Where(p =>
                p.CustomerName.ToLowerInvariant().Contains(q) ||
                p.CustomerPhone.ToLowerInvariant().Contains(q) ||
                p.CustomerEmail.ToLowerInvariant().Contains(q) ||
                (p.FacebookName != null && p.FacebookName.ToLowerInvariant().Contains(q)) ||
                p.PreferredCourt.ToLowerInvariant().Contains(q)
            ).ToList();
        }

        return Page();
    }

    public async Task<IActionResult> OnGetExportCsvAsync()
    {
        NormalizeYearMonth();
        var csvBytes = await _reportService.ExportCustomerDirectoryCsvAsync(CurrentOrgId, Year, Month);
        var fileName = $"Customer_Directory_{Year}_{Month:D2}.csv";
        return File(csvBytes, "text/csv; charset=utf-8", fileName);
    }

    private void NormalizeYearMonth()
    {
        var now = DateTime.UtcNow;
        if (Year <= 0) Year = now.Year;
        if (Month <= 0 || Month > 12) Month = now.Month;
    }
}
