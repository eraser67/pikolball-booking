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

    public async Task<CourtUtilizationReport> GetCourtUtilizationReportAsync(int organizationId, int year, int month)
    {
        if (year < 2000 || year > 2100) year = DateTime.UtcNow.Year;
        if (month < 1 || month > 12) month = DateTime.UtcNow.Month;

        var startDate = new DateOnly(year, month, 1);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var endDate = new DateOnly(year, month, daysInMonth);

        // Fetch organization name
        var orgName = await _context.Organizations
            .IgnoreQueryFilters()
            .Where(o => o.Id == organizationId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync() ?? $"Tenant #{organizationId}";

        // Strict tenant isolation: fetch courts belonging to this tenant
        var courts = await _context.Courts
            .AsNoTracking()
            .Where(c => c.OrganizationId == organizationId)
            .OrderBy(c => c.Name)
            .ToListAsync();

        int activeCourtsCount = courts.Count(c => c.Status == CourtStatus.Active);
        if (activeCourtsCount == 0 && courts.Count > 0)
        {
            activeCourtsCount = courts.Count;
        }

        // Determine tenant's daily operating hours from time slots and tenant overrides
        var masterSlots = await _context.TimeSlots
            .AsNoTracking()
            .Where(ts => ts.Status == TimeSlotStatus.Active)
            .ToListAsync();

        var tenantOverrides = await _context.TenantTimeSlots
            .AsNoTracking()
            .Where(tts => tts.OrganizationId == organizationId)
            .ToListAsync();

        var overrideMap = tenantOverrides.ToDictionary(tts => tts.TimeSlotId);

        decimal dailyOperatingHours = 0m;
        foreach (var slot in masterSlots)
        {
            if (overrideMap.TryGetValue(slot.Id, out var ov))
            {
                if (ov.IsDeleted || ov.Status != TimeSlotStatus.Active) continue;
                var st = ov.CustomStartTime ?? slot.StartTime;
                var et = ov.CustomEndTime ?? slot.EndTime;
                var span = (et - st).TotalHours;
                if (span <= 0 && et == TimeSpan.Zero) span = 24 - st.TotalHours;
                if (span > 0) dailyOperatingHours += (decimal)span;
            }
            else
            {
                var span = (slot.EndTime - slot.StartTime).TotalHours;
                if (span <= 0 && slot.EndTime == TimeSpan.Zero) span = 24 - slot.StartTime.TotalHours;
                if (span > 0) dailyOperatingHours += (decimal)span;
            }
        }

        // If no master slots configured, fallback to 16.0 hours/day (e.g. 6:00 AM - 10:00 PM)
        if (dailyOperatingHours <= 0)
        {
            dailyOperatingHours = 16.0m;
        }

        decimal totalCapacityHours = activeCourtsCount * dailyOperatingHours * daysInMonth;

        // Strict tenant isolation: fetch bookings for this tenant and month
        var bookings = await _context.Bookings
            .AsNoTracking()
            .Where(b => b.OrganizationId == organizationId
                        && b.BookingDate >= startDate
                        && b.BookingDate <= endDate
                        && (b.BookingStatus == BookingStatus.Confirmed || b.BookingStatus == BookingStatus.Completed))
            .Include(b => b.Court)
            .OrderBy(b => b.BookingDate)
            .ThenBy(b => b.StartTime)
            .ToListAsync();

        decimal totalBookedHours = bookings.Sum(b =>
        {
            var h = b.DurationHours ?? (decimal)(b.EndTime - b.StartTime).TotalHours;
            if (h <= 0 && b.EndTime == TimeSpan.Zero) h = (decimal)(TimeSpan.FromHours(24) - b.StartTime).TotalHours;
            return Math.Max(0m, h);
        });

        decimal totalRevenue = bookings.Sum(b => b.Price);

        var report = new CourtUtilizationReport
        {
            OrganizationId = organizationId,
            OrganizationName = orgName,
            Year = year,
            Month = month,
            ActiveCourtsCount = activeCourtsCount,
            DailyOperatingHours = dailyOperatingHours,
            TotalAvailableCapacityHours = totalCapacityHours,
            TotalBookedHours = totalBookedHours,
            TotalRevenue = totalRevenue
        };

        // 1. Hourly Utilization Breakdown (6:00 AM to 11:00 PM -> hours 6..22)
        var hourlyList = new List<HourlyUtilizationItem>();
        for (int h = 6; h <= 22; h++)
        {
            var slotStart = TimeSpan.FromHours(h);
            var slotEnd = TimeSpan.FromHours(h + 1);
            decimal slotBookedHours = 0m;
            decimal slotRevenue = 0m;
            int slotBookingsCount = 0;

            foreach (var b in bookings)
            {
                var overlapStart = b.StartTime > slotStart ? b.StartTime : slotStart;
                var overlapEnd = b.EndTime < slotEnd ? b.EndTime : slotEnd;

                if (overlapEnd > overlapStart)
                {
                    var overlapDuration = (decimal)(overlapEnd - overlapStart).TotalHours;
                    slotBookedHours += overlapDuration;

                    var totalDuration = b.DurationHours ?? (decimal)(b.EndTime - b.StartTime).TotalHours;
                    if (totalDuration <= 0 && b.EndTime == TimeSpan.Zero) totalDuration = (decimal)(TimeSpan.FromHours(24) - b.StartTime).TotalHours;
                    if (totalDuration > 0)
                    {
                        slotRevenue += b.Price * (overlapDuration / totalDuration);
                    }
                    slotBookingsCount++;
                }
            }

            decimal slotCapacity = activeCourtsCount * daysInMonth * 1.0m;
            hourlyList.Add(new HourlyUtilizationItem
            {
                Hour = h,
                BookingsCount = slotBookingsCount,
                BookedHours = slotBookedHours,
                Revenue = slotRevenue,
                CapacityHours = slotCapacity
            });
        }
        report.HourlyStats = hourlyList;

        // 2. Day of Week Breakdown (Monday to Sunday)
        var dowOccurrences = new Dictionary<DayOfWeek, int>();
        foreach (DayOfWeek d in Enum.GetValues<DayOfWeek>()) dowOccurrences[d] = 0;
        for (int day = 1; day <= daysInMonth; day++)
        {
            var dt = new DateOnly(year, month, day);
            dowOccurrences[dt.DayOfWeek]++;
        }

        var orderedDows = new[]
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
        };

        var dowStats = new List<DayOfWeekUtilizationItem>();
        foreach (var dow in orderedDows)
        {
            var dowBookings = bookings.Where(b => b.BookingDate.DayOfWeek == dow).ToList();
            var dowBookedHours = dowBookings.Sum(b =>
            {
                var h = b.DurationHours ?? (decimal)(b.EndTime - b.StartTime).TotalHours;
                if (h <= 0 && b.EndTime == TimeSpan.Zero) h = (decimal)(TimeSpan.FromHours(24) - b.StartTime).TotalHours;
                return Math.Max(0m, h);
            });

            decimal dowCapacity = dowOccurrences[dow] * activeCourtsCount * dailyOperatingHours;
            dowStats.Add(new DayOfWeekUtilizationItem
            {
                DayOfWeek = dow,
                OccurrenceCount = dowOccurrences[dow],
                TotalBookings = dowBookings.Count,
                BookedHours = dowBookedHours,
                Revenue = dowBookings.Sum(b => b.Price),
                CapacityHours = dowCapacity
            });
        }
        report.DayOfWeekStats = dowStats;

        // 3. Weekday vs Weekend Comparison
        var weekdayItems = dowStats.Where(d => d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday).ToList();
        var weekendItems = dowStats.Where(d => d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday).ToList();

        report.WeekdayVsWeekend = new WeekdayWeekendComparison
        {
            WeekdayBookedHours = weekdayItems.Sum(w => w.BookedHours),
            WeekdayRevenue = weekdayItems.Sum(w => w.Revenue),
            WeekdayCapacityHours = weekdayItems.Sum(w => w.CapacityHours),

            WeekendBookedHours = weekendItems.Sum(w => w.BookedHours),
            WeekendRevenue = weekendItems.Sum(w => w.Revenue),
            WeekendCapacityHours = weekendItems.Sum(w => w.CapacityHours)
        };

        // 4. Per-Court Details
        var courtDetails = new List<CourtUtilizationDetailItem>();
        foreach (var c in courts)
        {
            var courtBookings = bookings.Where(b => b.CourtId == c.Id).ToList();
            var courtBookedHours = courtBookings.Sum(b =>
            {
                var h = b.DurationHours ?? (decimal)(b.EndTime - b.StartTime).TotalHours;
                if (h <= 0 && b.EndTime == TimeSpan.Zero) h = (decimal)(TimeSpan.FromHours(24) - b.StartTime).TotalHours;
                return Math.Max(0m, h);
            });

            decimal courtCapacity = dailyOperatingHours * daysInMonth;
            courtDetails.Add(new CourtUtilizationDetailItem
            {
                CourtId = c.Id,
                CourtName = c.Name,
                Status = c.Status,
                TotalBookings = courtBookings.Count,
                BookedHours = courtBookedHours,
                AvailableCapacityHours = courtCapacity,
                Revenue = courtBookings.Sum(b => b.Price)
            });
        }
        report.CourtDetails = courtDetails;

        // 5. Peak & Off-Peak Identification
        var sortedHours = hourlyList.Where(h => h.CapacityHours > 0).OrderByDescending(h => h.UtilizationRate).ToList();
        if (sortedHours.Any())
        {
            var topHour = sortedHours.First();
            report.PeakHourWindow = $"{DateTime.Today.AddHours(topHour.Hour):h:mm tt} - {DateTime.Today.AddHours(topHour.Hour + 1):h:mm tt}";
            report.PeakHourUtilization = topHour.UtilizationRate;

            var bottomHour = sortedHours.Last();
            report.OffPeakHourWindow = $"{DateTime.Today.AddHours(bottomHour.Hour):h:mm tt} - {DateTime.Today.AddHours(bottomHour.Hour + 1):h:mm tt}";
            report.OffPeakHourUtilization = bottomHour.UtilizationRate;
        }

        var sortedDows = dowStats.Where(d => d.CapacityHours > 0).OrderByDescending(d => d.UtilizationRate).ToList();
        if (sortedDows.Any())
        {
            report.BusiestDayOfWeek = $"{sortedDows.First().DayName} ({sortedDows.First().UtilizationRate:F1}%)";
            report.QuietestDayOfWeek = $"{sortedDows.Last().DayName} ({sortedDows.Last().UtilizationRate:F1}%)";
        }

        // 6. Automated Strategic Recommendations
        var recs = new List<string>();
        if (report.PeakHourUtilization >= 65m)
        {
            recs.Add($"High Peak Demand: Occupancy reaches {report.PeakHourUtilization:F1}% during {report.PeakHourWindow}. Consider dynamic peak pricing (+10% to +20%) to optimize yield.");
        }
        if (report.OffPeakHourUtilization < 30m && !string.IsNullOrEmpty(report.OffPeakHourWindow))
        {
            recs.Add($"Off-Peak Capacity Opportunity: Utilization is low at {report.OffPeakHourUtilization:F1}% during {report.OffPeakHourWindow}. Host open-play clinics, training drills, or offer midday off-peak discount passes.");
        }
        if (report.WeekdayVsWeekend.WeekendUtilizationRate > report.WeekdayVsWeekend.WeekdayUtilizationRate + 15m)
        {
            recs.Add($"Weekend Demand Surge: Weekends are {(report.WeekdayVsWeekend.WeekendUtilizationRate - report.WeekdayVsWeekend.WeekdayUtilizationRate):F1}% more utilized than weekdays. Introduce corporate league packages on weekday evenings.");
        }
        if (courtDetails.Count > 1)
        {
            var topCourt = courtDetails.OrderByDescending(c => c.RevPAH).First();
            var bottomCourt = courtDetails.OrderBy(c => c.RevPAH).First();
            if (topCourt.RevPAH > bottomCourt.RevPAH * 1.3m)
            {
                recs.Add($"Court Revenue Imbalance: {topCourt.CourtName} generates ₱{topCourt.RevPAH:N2}/hr vs {bottomCourt.CourtName} at ₱{bottomCourt.RevPAH:N2}/hr. Inspect amenities or surface preferences driving {bottomCourt.CourtName}'s lower demand.");
            }
        }
        if (!recs.Any())
        {
            recs.Add("Court utilization is well-balanced across operating hours and days of the week. Continue monitoring seasonal changes.");
        }
        report.StrategicRecommendations = recs;

        return report;
    }

    public async Task<byte[]> ExportCourtUtilizationCsvAsync(int organizationId, int year, int month)
    {
        var report = await GetCourtUtilizationReportAsync(organizationId, year, month);

        var sb = new StringBuilder();

        // Metadata Header
        sb.AppendLine($"# Court Utilization & Peak Hours Report");
        sb.AppendLine($"# Organization: {EscapeCsv(report.OrganizationName)} (ID: {report.OrganizationId})");
        sb.AppendLine($"# Period: {report.MonthName}");
        sb.AppendLine($"# Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"# Active Courts: {report.ActiveCourtsCount}");
        sb.AppendLine($"# Total Operating Hours Available: {report.TotalAvailableCapacityHours:N1} hrs");
        sb.AppendLine($"# Total Booked Hours: {report.TotalBookedHours:N1} hrs");
        sb.AppendLine($"# Overall Facility Utilization Rate: {report.OverallUtilizationRate:F1}%");
        sb.AppendLine($"# Total Realized Revenue: ₱{report.TotalRevenue:N2}");
        sb.AppendLine($"# Revenue Per Available Hour (RevPAH): ₱{report.RevPAH:N2}");
        sb.AppendLine($"# Peak Hour: {EscapeCsv(report.PeakHourWindow)} ({report.PeakHourUtilization:F1}%)");
        sb.AppendLine($"# Quietest Hour: {EscapeCsv(report.OffPeakHourWindow)} ({report.OffPeakHourUtilization:F1}%)");
        sb.AppendLine();

        // 1. Hourly Utilization Table
        sb.AppendLine("## Hourly Distribution & Peak Demand");
        sb.AppendLine("Hour,Time Window,Bookings Count,Booked Hours,Revenue,Capacity Hours,Utilization %,Demand Level");
        foreach (var h in report.HourlyStats)
        {
            sb.AppendLine(string.Join(",",
                h.Hour,
                EscapeCsv(h.TimeLabel),
                h.BookingsCount,
                h.BookedHours.ToString("F2"),
                h.Revenue.ToString("F2"),
                h.CapacityHours.ToString("F2"),
                h.UtilizationRate.ToString("F1") + "%",
                h.DemandLevel
            ));
        }
        sb.AppendLine();

        // 2. Day of Week Table
        sb.AppendLine("## Day of the Week Performance");
        sb.AppendLine("Day of Week,Days in Month,Total Bookings,Booked Hours,Revenue,Capacity Hours,Utilization %");
        foreach (var d in report.DayOfWeekStats)
        {
            sb.AppendLine(string.Join(",",
                d.DayName,
                d.OccurrenceCount,
                d.TotalBookings,
                d.BookedHours.ToString("F2"),
                d.Revenue.ToString("F2"),
                d.CapacityHours.ToString("F2"),
                d.UtilizationRate.ToString("F1") + "%"
            ));
        }
        sb.AppendLine();

        // 3. Court Utilization Details
        sb.AppendLine("## Per-Court Utilization & RevPAH");
        sb.AppendLine("Court ID,Court Name,Status,Total Bookings,Booked Hours,Capacity Hours,Utilization %,Revenue,RevPAH");
        foreach (var c in report.CourtDetails)
        {
            sb.AppendLine(string.Join(",",
                c.CourtId,
                EscapeCsv(c.CourtName),
                c.Status.ToString(),
                c.TotalBookings,
                c.BookedHours.ToString("F2"),
                c.AvailableCapacityHours.ToString("F2"),
                c.UtilizationRate.ToString("F1") + "%",
                c.Revenue.ToString("F2"),
                c.RevPAH.ToString("F2")
            ));
        }

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
