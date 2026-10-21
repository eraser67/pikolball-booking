using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Data;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Reports;

[Authorize(Policy = TenantAdminAuthorization.TenantAdminPolicy)]
public class AttendanceModel : PageModel
{
    private readonly IReportService _reportService;
    private readonly ITenantContext _tenantContext;
    private readonly ApplicationDbContext _context;

    public AttendanceModel(
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
    public string? Filter { get; set; } // "all", "checkedin", "noshow", "unmarked"

    [BindProperty(SupportsGet = true)]
    public string? SearchQuery { get; set; }

    public AttendanceReport Report { get; set; } = new();

    public int CurrentOrgId => _tenantContext.OrganizationId ?? _context.CurrentOrganizationId ?? 1;

    public async Task<IActionResult> OnGetAsync()
    {
        NormalizeYearMonth();
        Report = await _reportService.GetAttendanceReportAsync(CurrentOrgId, Year, Month);

        // Filter records if specified
        if (!string.IsNullOrWhiteSpace(Filter))
        {
            switch (Filter.ToLowerInvariant())
            {
                case "checkedin":
                    Report.Records = Report.Records.Where(r => r.CheckedIn).ToList();
                    break;
                case "noshow":
                    Report.Records = Report.Records.Where(r => r.IsNoShow).ToList();
                    break;
                case "unmarked":
                    Report.Records = Report.Records.Where(r => !r.CheckedIn && !r.IsNoShow).ToList();
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var q = SearchQuery.Trim().ToLowerInvariant();
            Report.Records = Report.Records.Where(r =>
                r.BookingReference.ToLowerInvariant().Contains(q) ||
                r.CustomerName.ToLowerInvariant().Contains(q) ||
                r.CustomerPhone.ToLowerInvariant().Contains(q) ||
                r.CustomerEmail.ToLowerInvariant().Contains(q) ||
                r.CourtName.ToLowerInvariant().Contains(q)
            ).ToList();
        }

        return Page();
    }

    public async Task<IActionResult> OnGetExportCsvAsync()
    {
        NormalizeYearMonth();
        var csvBytes = await _reportService.ExportAttendanceReportCsvAsync(CurrentOrgId, Year, Month);
        var fileName = $"Attendance_Report_{Year}_{Month:D2}.csv";
        return File(csvBytes, "text/csv; charset=utf-8", fileName);
    }

    private void NormalizeYearMonth()
    {
        var now = DateTime.UtcNow;
        if (Year <= 0) Year = now.Year;
        if (Month <= 0 || Month > 12) Month = now.Month;
    }
}
