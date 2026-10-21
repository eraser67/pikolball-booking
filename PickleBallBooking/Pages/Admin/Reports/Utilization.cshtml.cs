using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Data;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Reports;

[Authorize(Policy = TenantAdminAuthorization.TenantAdminPolicy)]
public class UtilizationModel : PageModel
{
    private readonly IReportService _reportService;
    private readonly ITenantContext _tenantContext;
    private readonly ApplicationDbContext _context;

    public UtilizationModel(
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

    public CourtUtilizationReport Report { get; set; } = new();

    public int CurrentOrgId => _tenantContext.OrganizationId ?? _context.CurrentOrganizationId ?? 1;

    public async Task<IActionResult> OnGetAsync()
    {
        NormalizeYearMonth();
        Report = await _reportService.GetCourtUtilizationReportAsync(CurrentOrgId, Year, Month);
        return Page();
    }

    public async Task<IActionResult> OnGetExportCsvAsync()
    {
        NormalizeYearMonth();
        var csvBytes = await _reportService.ExportCourtUtilizationCsvAsync(CurrentOrgId, Year, Month);
        var fileName = $"Court_Utilization_{Year}_{Month:D2}.csv";
        return File(csvBytes, "text/csv; charset=utf-8", fileName);
    }

    private void NormalizeYearMonth()
    {
        var now = DateTime.UtcNow;
        if (Year <= 0) Year = now.Year;
        if (Month <= 0 || Month > 12) Month = now.Month;
    }
}
