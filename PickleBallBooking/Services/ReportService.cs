using System.Text;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public class ReportService : IReportService
{
    private readonly ApplicationDbContext _context;

    public ReportService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<MonthlyBookingReport> GetMonthlyReportAsync(int organizationId, int year, int month)
    {
        if (year < 2000 || year > 2100) year = DateTime.UtcNow.Year;
        if (month < 1 || month > 12) month = DateTime.UtcNow.Month;

        var startDate = new DateOnly(year, month, 1);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var endDate = new DateOnly(year, month, daysInMonth);

        // Fetch organization info (using IgnoreQueryFilters only to load org metadata if needed)
        var orgName = await _context.Organizations
            .IgnoreQueryFilters()
            .Where(o => o.Id == organizationId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync() ?? $"Tenant #{organizationId}";

        // Strict tenant isolation: explicitly filter by OrganizationId
        var bookings = await _context.Bookings
            .AsNoTracking()
            .Where(b => b.OrganizationId == organizationId
                        && b.BookingDate >= startDate
                        && b.BookingDate <= endDate)
            .Include(b => b.Court)
            .OrderBy(b => b.BookingDate)
            .ThenBy(b => b.StartTime)
            .ToListAsync();

        var bookingIds = bookings.Select(b => b.Id).ToList();

        // Payments linked to these bookings
        var payments = await _context.Payments
            .AsNoTracking()
            .Where(p => p.OrganizationId == organizationId && bookingIds.Contains(p.BookingId))
            .ToDictionaryAsync(p => p.BookingId);

        var report = new MonthlyBookingReport
        {
            OrganizationId = organizationId,
            OrganizationName = orgName,
            Year = year,
            Month = month,
            TotalBookingsCount = bookings.Count
        };

        // Populate KPIs
        foreach (var b in bookings)
        {
            switch (b.BookingStatus)
            {
                case BookingStatus.Confirmed:
                    report.ConfirmedCount++;
                    report.TotalRevenueCollected += b.Price;
                    break;
                case BookingStatus.Completed:
                    report.CompletedCount++;
                    report.TotalRevenueCollected += b.Price;
                    break;
                case BookingStatus.Pending:
                    report.PendingCount++;
                    report.PendingRevenue += b.Price;
                    break;
                case BookingStatus.Cancelled:
                    report.CancelledCount++;
                    report.LostCancelledRevenue += b.Price;
                    break;
            }

            if (b.IsNoShow)
            {
                report.NoShowCount++;
            }

            // Duration calculation
            decimal hours = b.DurationHours ?? (decimal)(b.EndTime - b.StartTime).TotalHours;
            if (hours <= 0 && b.EndTime == TimeSpan.Zero)
            {
                // Overnight ending at midnight
                hours = (decimal)(TimeSpan.FromHours(24) - b.StartTime).TotalHours;
            }
            if (b.BookingStatus is BookingStatus.Confirmed or BookingStatus.Completed)
            {
                report.TotalBookedHours += Math.Max(0m, hours);
            }
        }

        // Daily breakdown
        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = new DateOnly(year, month, day);
            var dayBookings = bookings.Where(b => b.BookingDate == date).ToList();
            var confirmedRevenue = dayBookings
                .Where(b => b.BookingStatus is BookingStatus.Confirmed or BookingStatus.Completed)
                .Sum(b => b.Price);

            report.DailyStats.Add(new DailyReportItem
            {
                Date = date,
                TotalBookings = dayBookings.Count,
                ConfirmedOrCompleted = dayBookings.Count(b => b.BookingStatus is BookingStatus.Confirmed or BookingStatus.Completed),
                Revenue = confirmedRevenue
            });
        }

        // Court breakdown
        var courtGroups = bookings
            .GroupBy(b => new { b.CourtId, CourtName = b.Court?.Name ?? $"Court #{b.CourtId}" })
            .OrderBy(g => g.Key.CourtName);

        foreach (var cg in courtGroups)
        {
            var confirmedBookings = cg.Where(b => b.BookingStatus is BookingStatus.Confirmed or BookingStatus.Completed).ToList();
            decimal courtHours = confirmedBookings.Sum(b =>
            {
                var h = b.DurationHours ?? (decimal)(b.EndTime - b.StartTime).TotalHours;
                if (h <= 0 && b.EndTime == TimeSpan.Zero) h = (decimal)(TimeSpan.FromHours(24) - b.StartTime).TotalHours;
                return Math.Max(0m, h);
            });

            report.CourtStats.Add(new CourtReportItem
            {
                CourtId = cg.Key.CourtId,
                CourtName = cg.Key.CourtName,
                TotalBookings = confirmedBookings.Count,
                TotalHours = courtHours,
                TotalRevenue = confirmedBookings.Sum(b => b.Price)
            });
        }

        // Payment Method breakdown
        var paymentGroups = bookings
            .Where(b => b.BookingStatus is BookingStatus.Confirmed or BookingStatus.Completed)
            .GroupBy(b =>
            {
                if (payments.TryGetValue(b.Id, out var p))
                {
                    return p.PaymentMethod.ToString();
                }
                return b.SelectedPaymentMethod?.ToString() ?? "Unspecified";
            })
            .OrderByDescending(g => g.Sum(b => b.Price));

        foreach (var pg in paymentGroups)
        {
            report.PaymentMethodStats.Add(new PaymentMethodReportItem
            {
                MethodName = pg.Key,
                TransactionCount = pg.Count(),
                TotalAmount = pg.Sum(b => b.Price)
            });
        }

        // Booking Ledger
        foreach (var b in bookings)
        {
            payments.TryGetValue(b.Id, out var payment);

            report.Bookings.Add(new BookingReportItem
            {
                BookingId = b.Id,
                BookingReference = b.BookingReference,
                BookingDate = b.BookingDate,
                TimeRange = AppClock.To12HourRange(b.StartTime, b.EndTime),
                CourtName = b.Court?.Name ?? $"Court #{b.CourtId}",
                CustomerName = b.CustomerName,
                CustomerPhone = b.CustomerPhone,
                CustomerEmail = b.CustomerEmail,
                Price = b.Price,
                Status = b.BookingStatus,
                PaymentMethod = payment != null ? payment.PaymentMethod.ToString() : b.SelectedPaymentMethod?.ToString(),
                PaymentReference = payment?.ReferenceNumber,
                PaymentStatus = payment?.PaymentStatus,
                CheckedIn = b.CheckedInAt != null,
                IsNoShow = b.IsNoShow,
                CreatedAt = b.CreatedAt
            });
        }

        return report;
    }

    public async Task<byte[]> ExportMonthlyReportCsvAsync(int organizationId, int year, int month)
    {
        var report = await GetMonthlyReportAsync(organizationId, year, month);

        var sb = new StringBuilder();

        // Metadata Header
        sb.AppendLine($"# Monthly Booking & Revenue Report");
        sb.AppendLine($"# Organization: {EscapeCsv(report.OrganizationName)} (ID: {report.OrganizationId})");
        sb.AppendLine($"# Period: {report.MonthName}");
        sb.AppendLine($"# Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"# Total Bookings: {report.TotalBookingsCount}");
        sb.AppendLine($"# Confirmed/Completed: {report.ConfirmedCount + report.CompletedCount}");
        sb.AppendLine($"# Total Revenue Collected: ₱{report.TotalRevenueCollected:N2}");
        sb.AppendLine();

        // CSV Table Columns
        sb.AppendLine("Reference,Date,Time Range,Court,Customer Name,Phone,Email,Status,Price,Payment Method,Payment Reference,Payment Status,Checked In,No Show,Booking Created");

        foreach (var b in report.Bookings)
        {
            sb.AppendLine(string.Join(",",
                EscapeCsv(b.BookingReference),
                b.BookingDate.ToString("yyyy-MM-dd"),
                EscapeCsv(b.TimeRange),
                EscapeCsv(b.CourtName),
                EscapeCsv(b.CustomerName),
                EscapeCsv(b.CustomerPhone),
                EscapeCsv(b.CustomerEmail),
                b.Status.ToString(),
                b.Price.ToString("F2"),
                EscapeCsv(b.PaymentMethod ?? ""),
                EscapeCsv(b.PaymentReference ?? ""),
                b.PaymentStatus?.ToString() ?? "",
                b.CheckedIn ? "Yes" : "No",
                b.IsNoShow ? "Yes" : "No",
                b.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
            ));
        }

        // Return with UTF-8 BOM so Excel opens properly with special characters and symbols
        var utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return utf8WithBom.GetBytes(sb.ToString());
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
