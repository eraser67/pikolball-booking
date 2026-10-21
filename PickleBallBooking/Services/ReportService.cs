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

    public async Task<AttendanceReport> GetAttendanceReportAsync(int organizationId, int year, int month)
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

        // Strict tenant isolation: fetch only this tenant's confirmed/completed bookings for the month
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

        int totalEligible = bookings.Count;
        int checkedInCount = bookings.Count(b => b.CheckedInAt.HasValue && !b.IsNoShow);
        int noShowCount = bookings.Count(b => b.IsNoShow);
        int unmarkedCount = bookings.Count(b => !b.CheckedInAt.HasValue && !b.IsNoShow);

        decimal noShowLoss = bookings.Where(b => b.IsNoShow).Sum(b => b.Price);
        decimal checkedInRevenue = bookings.Where(b => b.CheckedInAt.HasValue && !b.IsNoShow).Sum(b => b.Price);

        // Punctuality calculation (minutes relative to scheduled local start time)
        var punctualityList = new List<double>();
        var records = new List<AttendanceRecordItem>();

        foreach (var b in bookings)
        {
            int? punctualityMin = null;
            DateTime? localCheckIn = null;

            if (b.CheckedInAt.HasValue)
            {
                localCheckIn = AppClock.ToPhilippineTime(b.CheckedInAt.Value);
                if (!b.IsNoShow)
                {
                    var scheduledLocal = b.BookingDate.ToDateTime(TimeOnly.FromTimeSpan(b.StartTime));
                    var delta = (localCheckIn.Value - scheduledLocal).TotalMinutes;
                    punctualityMin = (int)Math.Round(delta);
                    punctualityList.Add(delta);
                }
            }

            records.Add(new AttendanceRecordItem
            {
                BookingId = b.Id,
                BookingReference = b.BookingReference,
                BookingDate = b.BookingDate,
                TimeRange = $"{AppClock.To12Hour(b.StartTime)} - {AppClock.To12Hour(b.EndTime)}",
                CourtName = b.Court?.Name ?? $"Court #{b.CourtId}",
                CustomerName = b.CustomerName,
                CustomerPhone = b.CustomerPhone,
                CustomerEmail = b.CustomerEmail,
                Price = b.Price,
                BookingStatus = b.BookingStatus,
                CheckedIn = b.CheckedInAt.HasValue && !b.IsNoShow,
                CheckedInAtLocal = localCheckIn,
                CheckInMethod = b.CheckInMethod,
                CheckedInByUserId = b.CheckedInByUserId,
                IsNoShow = b.IsNoShow,
                PunctualityMinutes = punctualityMin
            });
        }

        double? avgPunctuality = punctualityList.Any() ? Math.Round(punctualityList.Average(), 1) : null;

        var report = new AttendanceReport
        {
            OrganizationId = organizationId,
            OrganizationName = orgName,
            Year = year,
            Month = month,
            TotalEligibleBookings = totalEligible,
            CheckedInCount = checkedInCount,
            NoShowCount = noShowCount,
            UnmarkedCount = unmarkedCount,
            NoShowRevenueLoss = noShowLoss,
            RealizedCheckedInRevenue = checkedInRevenue,
            AveragePunctualityMinutes = avgPunctuality,
            Records = records
        };

        // 1. Check-in method breakdown
        var methodGroups = bookings
            .Where(b => b.CheckedInAt.HasValue && !b.IsNoShow)
            .GroupBy(b => b.CheckInMethod?.ToString() ?? "Unspecified")
            .Select(g => new CheckInMethodReportItem
            {
                MethodName = g.Key == "QrScan" ? "QR Pass Scan" :
                             g.Key == "AdminManual" ? "Staff Manual Check-In" :
                             g.Key == "WalkIn" ? "Walk-In / Verbal" : g.Key,
                Count = g.Count(),
                SharePercentage = checkedInCount > 0 ? Math.Round((decimal)g.Count() / checkedInCount * 100m, 1) : 0m
            })
            .OrderByDescending(m => m.Count)
            .ToList();
        report.MethodStats = methodGroups;

        // 2. Day of Week Attendance
        var orderedDows = new[]
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
        };

        var dowStats = new List<DayOfWeekAttendanceItem>();
        foreach (var dow in orderedDows)
        {
            var dowBookings = bookings.Where(b => b.BookingDate.DayOfWeek == dow).ToList();
            dowStats.Add(new DayOfWeekAttendanceItem
            {
                DayOfWeek = dow,
                TotalBookings = dowBookings.Count,
                CheckedInCount = dowBookings.Count(b => b.CheckedInAt.HasValue && !b.IsNoShow),
                NoShowCount = dowBookings.Count(b => b.IsNoShow)
            });
        }
        report.DayOfWeekStats = dowStats;

        // 3. Customer Reliability & Repeat No-Shows
        var customerGroups = bookings
            .GroupBy(b => !string.IsNullOrWhiteSpace(b.CustomerPhone) ? b.CustomerPhone : b.CustomerName.Trim().ToLowerInvariant())
            .Select(g =>
            {
                var first = g.First();
                var total = g.Count();
                var cIn = g.Count(b => b.CheckedInAt.HasValue && !b.IsNoShow);
                var ns = g.Count(b => b.IsNoShow);
                return new CustomerReliabilityItem
                {
                    CustomerName = first.CustomerName,
                    CustomerPhone = first.CustomerPhone,
                    CustomerEmail = first.CustomerEmail,
                    TotalBookings = total,
                    CheckedInCount = cIn,
                    NoShowCount = ns
                };
            })
            .OrderByDescending(c => c.NoShowCount)
            .ThenBy(c => c.ReliabilityRate)
            .ThenByDescending(c => c.TotalBookings)
            .Take(30)
            .ToList();
        report.CustomerReliabilityStats = customerGroups;

        // 4. Strategic Insights
        var insights = new List<string>();
        if (report.NoShowRate >= 10m)
        {
            insights.Add($"High No-Show Rate: {report.NoShowRate:F1}% of bookings resulted in no-shows (₱{report.NoShowRevenueLoss:N2} lost revenue). Consider implementing a 50% non-refundable deposit or booking cooldown for no-shows.");
        }
        if (report.CheckInRate >= 80m)
        {
            insights.Add($"Strong Turnout Fidelity: {report.CheckInRate:F1}% check-in rate demonstrates high customer engagement and commitment.");
        }
        if (report.AveragePunctualityMinutes.HasValue)
        {
            if (report.AveragePunctualityMinutes.Value <= -5)
            {
                insights.Add($"Punctual Players: Customers arrive an average of {Math.Abs(report.AveragePunctualityMinutes.Value):F1} minutes before their scheduled court time.");
            }
            else if (report.AveragePunctualityMinutes.Value >= 10)
            {
                insights.Add($"Late Arrival Tendency: Average check-in is {report.AveragePunctualityMinutes.Value:F1} minutes after slot start. Ensure automated SMS/email reminders are sent 1 hour prior.");
            }
        }
        var repeatNoShowCustomers = customerGroups.Count(c => c.NoShowCount >= 2);
        if (repeatNoShowCustomers > 0)
        {
            insights.Add($"Repeat No-Show Alert: {repeatNoShowCustomers} customer(s) have 2 or more no-shows this month. Check Customer Reliability list to flag repeat offenders.");
        }
        if (!insights.Any())
        {
            insights.Add("Attendance and check-in rates are healthy. Continue scanning customer QR passes for swift arrival tracking.");
        }
        report.StrategicInsights = insights;

        return report;
    }

    public async Task<byte[]> ExportAttendanceReportCsvAsync(int organizationId, int year, int month)
    {
        var report = await GetAttendanceReportAsync(organizationId, year, month);

        var sb = new StringBuilder();

        // Metadata Header
        sb.AppendLine($"# Attendance, Check-In & No-Show Report");
        sb.AppendLine($"# Organization: {EscapeCsv(report.OrganizationName)} (ID: {report.OrganizationId})");
        sb.AppendLine($"# Period: {report.MonthName}");
        sb.AppendLine($"# Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"# Total Eligible Bookings: {report.TotalEligibleBookings}");
        sb.AppendLine($"# Checked-In: {report.CheckedInCount} ({report.CheckInRate:F1}%)");
        sb.AppendLine($"# No-Shows: {report.NoShowCount} ({report.NoShowRate:F1}%)");
        sb.AppendLine($"# Unmarked / Pending: {report.UnmarkedCount}");
        sb.AppendLine($"# Realized Checked-In Revenue: ₱{report.RealizedCheckedInRevenue:N2}");
        sb.AppendLine($"# No-Show Revenue Loss: ₱{report.NoShowRevenueLoss:N2}");
        sb.AppendLine($"# Average Arrival Delta: {(report.AveragePunctualityMinutes.HasValue ? report.AveragePunctualityMinutes.Value.ToString("F1") + " mins" : "N/A")}");
        sb.AppendLine();

        // 1. Audit Ledger Table
        sb.AppendLine("## Attendance Audit Ledger");
        sb.AppendLine("Reference,Date,Time Range,Court,Customer Name,Phone,Email,Price,Status,Attendance Status,Check-In Time (Local),Check-In Method,Punctuality (Mins),Checked In By");

        foreach (var r in report.Records)
        {
            var attendanceStatus = r.IsNoShow ? "No-Show" : r.CheckedIn ? "Checked-In" : "Pending Arrival";
            var punctualityStr = r.PunctualityMinutes.HasValue 
                ? (r.PunctualityMinutes.Value < 0 ? $"{Math.Abs(r.PunctualityMinutes.Value)}m Early" : r.PunctualityMinutes.Value == 0 ? "On Time" : $"{r.PunctualityMinutes.Value}m Late") 
                : "";

            sb.AppendLine(string.Join(",",
                EscapeCsv(r.BookingReference),
                r.BookingDate.ToString("yyyy-MM-dd"),
                EscapeCsv(r.TimeRange),
                EscapeCsv(r.CourtName),
                EscapeCsv(r.CustomerName),
                EscapeCsv(r.CustomerPhone),
                EscapeCsv(r.CustomerEmail),
                r.Price.ToString("F2"),
                r.BookingStatus.ToString(),
                attendanceStatus,
                r.CheckedInAtLocal.HasValue ? r.CheckedInAtLocal.Value.ToString("yyyy-MM-dd HH:mm:ss") : "",
                EscapeCsv(r.CheckInMethod?.ToString() ?? ""),
                EscapeCsv(punctualityStr),
                EscapeCsv(r.CheckedInByUserId ?? "")
            ));
        }

        var utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return utf8WithBom.GetBytes(sb.ToString());
    }

    public async Task<CustomerInsightsReport> GetCustomerInsightsReportAsync(int organizationId, int year, int month)
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

        // Strict tenant isolation: fetch current month confirmed & completed bookings
        var monthBookings = await _context.Bookings
            .AsNoTracking()
            .Where(b => b.OrganizationId == organizationId
                        && b.BookingDate >= startDate
                        && b.BookingDate <= endDate
                        && (b.BookingStatus == BookingStatus.Confirmed || b.BookingStatus == BookingStatus.Completed))
            .Include(b => b.Court)
            .OrderBy(b => b.BookingDate)
            .ThenBy(b => b.StartTime)
            .ToListAsync();

        // Strict tenant isolation: fetch prior confirmed & completed bookings for this tenant
        var priorBookings = await _context.Bookings
            .AsNoTracking()
            .Where(b => b.OrganizationId == organizationId
                        && b.BookingDate < startDate
                        && (b.BookingStatus == BookingStatus.Confirmed || b.BookingStatus == BookingStatus.Completed))
            .Select(b => new
            {
                b.CustomerName,
                b.CustomerPhone,
                b.CustomerEmail,
                b.BookingDate,
                b.Price
            })
            .ToListAsync();

        var priorCustomerKeys = new HashSet<string>(priorBookings.Select(b =>
            !string.IsNullOrWhiteSpace(b.CustomerPhone) ? b.CustomerPhone.Trim() : b.CustomerName.Trim().ToLowerInvariant()));

        // Group month bookings by unique customer key (phone preferred, or name)
        var monthGroups = monthBookings
            .GroupBy(b => !string.IsNullOrWhiteSpace(b.CustomerPhone) ? b.CustomerPhone.Trim() : b.CustomerName.Trim().ToLowerInvariant())
            .ToList();

        int totalUniqueCustomers = monthGroups.Count;
        int returningCustomers = monthGroups.Count(g => priorCustomerKeys.Contains(g.Key));
        int newCustomers = totalUniqueCustomers - returningCustomers;
        decimal totalRevenue = monthBookings.Sum(b => b.Price);

        int vipCount = monthGroups.Count(g => g.Count() >= 5);
        int regularCount = monthGroups.Count(g => g.Count() >= 2 && g.Count() < 5);
        int occasionalCount = monthGroups.Count(g => g.Count() == 1);

        // Build VIP Players Ranking
        var topPlayers = new List<VipPlayerItem>();
        int rank = 1;

        var sortedGroups = monthGroups
            .OrderByDescending(g => g.Sum(b => b.Price))
            .ThenByDescending(g => g.Count())
            .ToList();

        foreach (var g in sortedGroups)
        {
            var first = g.First();
            var totalSpend = g.Sum(b => b.Price);
            var totalHours = g.Sum(b =>
            {
                var h = b.DurationHours ?? (decimal)(b.EndTime - b.StartTime).TotalHours;
                if (h <= 0 && b.EndTime == TimeSpan.Zero) h = (decimal)(TimeSpan.FromHours(24) - b.StartTime).TotalHours;
                return Math.Max(0m, h);
            });

            // Calculate Preferred Court (most frequently booked court by this player)
            var preferredCourt = g
                .GroupBy(b => b.Court?.Name ?? "Court")
                .OrderByDescending(cg => cg.Count())
                .First().Key;

            // Calculate Preferred Playing Time
            var preferredTime = g
                .GroupBy(b => AppClock.To12Hour(b.StartTime))
                .OrderByDescending(tg => tg.Count())
                .First().Key;

            topPlayers.Add(new VipPlayerItem
            {
                Rank = rank++,
                CustomerName = first.CustomerName,
                CustomerPhone = first.CustomerPhone,
                CustomerEmail = first.CustomerEmail,
                FacebookName = first.FacebookName,
                TotalBookings = g.Count(),
                TotalHours = totalHours,
                TotalSpend = totalSpend,
                PreferredCourt = preferredCourt,
                PreferredTimeSlot = preferredTime,
                FirstBookingDate = g.Min(b => b.BookingDate),
                LatestBookingDate = g.Max(b => b.BookingDate),
                IsNewCustomerThisMonth = !priorCustomerKeys.Contains(g.Key)
            });
        }

        // Top 10 customer revenue share
        decimal topTenRevenue = topPlayers.Take(10).Sum(p => p.TotalSpend);
        decimal topTenShare = totalRevenue > 0 ? Math.Round(topTenRevenue / totalRevenue * 100m, 1) : 0m;

        // Detect At-Risk / Inactive Customers (booked in 90 days before startDate, but 0 bookings this month)
        var cutoffDate = startDate.AddDays(-90);
        var monthCustomerKeys = new HashSet<string>(monthGroups.Select(g => g.Key));

        var atRiskCustomers = priorBookings
            .Where(b => b.BookingDate >= cutoffDate)
            .GroupBy(b => !string.IsNullOrWhiteSpace(b.CustomerPhone) ? b.CustomerPhone.Trim() : b.CustomerName.Trim().ToLowerInvariant())
            .Where(g => !monthCustomerKeys.Contains(g.Key))
            .Select(g =>
            {
                var first = g.First();
                var lastDate = g.Max(b => b.BookingDate);
                var daysSince = (int)(startDate.ToDateTime(TimeOnly.MinValue) - lastDate.ToDateTime(TimeOnly.MinValue)).TotalDays;
                return new InactiveCustomerItem
                {
                    CustomerName = first.CustomerName,
                    CustomerPhone = first.CustomerPhone,
                    CustomerEmail = first.CustomerEmail,
                    LifetimeBookings = g.Count(),
                    LifetimeSpend = g.Sum(b => b.Price),
                    LastActiveDate = lastDate,
                    DaysSinceLastBooking = Math.Max(1, daysSince)
                };
            })
            .OrderByDescending(c => c.LifetimeSpend)
            .ThenByDescending(c => c.LifetimeBookings)
            .Take(30)
            .ToList();

        // Strategic Recommendations
        var recs = new List<string>();
        if (topTenShare >= 50m && topPlayers.Count >= 10)
        {
            recs.Add($"High Revenue Concentration: Top 10 players generate {topTenShare:F1}% of monthly revenue. Consider a VIP loyalty perks program or invitation-only club events to maintain their retention.");
        }
        if (totalUniqueCustomers > 0)
        {
            var repeatRate = Math.Round((decimal)returningCustomers / totalUniqueCustomers * 100m, 1);
            if (repeatRate < 35m)
            {
                recs.Add($"Low Repeat Rate ({repeatRate:F1}%): Most bookings come from one-time players. Offer a bounce-back voucher (e.g. 10% off if booked within 14 days) to drive repeat frequency.");
            }
            else
            {
                recs.Add($"Healthy Player Retention: {repeatRate:F1}% repeat booking rate indicates satisfied, loyal customers.");
            }
        }
        if (atRiskCustomers.Count >= 5)
        {
            recs.Add($"Win-Back Opportunity: {atRiskCustomers.Count} previously active players haven't booked this month. Download the At-Risk directory to send re-engagement SMS or off-peak promotions.");
        }
        if (newCustomers > 0)
        {
            recs.Add($"New Player Acquisition: Welcomed {newCustomers} first-time players this month ({Math.Round((decimal)newCustomers / Math.Max(1, totalUniqueCustomers) * 100m, 1)}% of customer base).");
        }
        if (!recs.Any())
        {
            recs.Add("Customer activity is steady. Continue nurturing repeat player relationships with seasonal round robins.");
        }

        return new CustomerInsightsReport
        {
            OrganizationId = organizationId,
            OrganizationName = orgName,
            Year = year,
            Month = month,
            TotalUniqueCustomersInMonth = totalUniqueCustomers,
            NewCustomersInMonth = newCustomers,
            ReturningCustomersInMonth = returningCustomers,
            TotalRevenue = totalRevenue,
            TopTenCustomerRevenueShare = topTenShare,
            VipPlayersCount = vipCount,
            RegularPlayersCount = regularCount,
            OccasionalPlayersCount = occasionalCount,
            TopPlayers = topPlayers,
            AtRiskCustomers = atRiskCustomers,
            StrategicRecommendations = recs
        };
    }

    public async Task<byte[]> ExportCustomerDirectoryCsvAsync(int organizationId, int year, int month)
    {
        var report = await GetCustomerInsightsReportAsync(organizationId, year, month);

        var sb = new StringBuilder();

        // Metadata Header
        sb.AppendLine($"# Customer & Repeat Player Directory");
        sb.AppendLine($"# Organization: {EscapeCsv(report.OrganizationName)} (ID: {report.OrganizationId})");
        sb.AppendLine($"# Period: {report.MonthName}");
        sb.AppendLine($"# Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"# Total Unique Players: {report.TotalUniqueCustomersInMonth}");
        sb.AppendLine($"# Repeat Retention Rate: {report.RepeatCustomerRate:F1}%");
        sb.AppendLine($"# VIP Players (>= 5 bookings): {report.VipPlayersCount}");
        sb.AppendLine($"# Regular Players (2-4 bookings): {report.RegularPlayersCount}");
        sb.AppendLine($"# New Players This Month: {report.NewCustomersInMonth}");
        sb.AppendLine($"# Total Realized Spend: ₱{report.TotalRevenue:N2}");
        sb.AppendLine();

        // 1. VIP & Active Players Directory
        sb.AppendLine("## Active & Frequent Players Directory");
        sb.AppendLine("Rank,Customer Name,Phone,Email,Facebook,Loyalty Tier,Customer Type,Monthly Bookings,Total Hours,Total Spend (PHP),Preferred Court,Preferred Time,First Booking,Latest Booking");

        foreach (var p in report.TopPlayers)
        {
            sb.AppendLine(string.Join(",",
                p.Rank,
                EscapeCsv(p.CustomerName),
                EscapeCsv(p.CustomerPhone),
                EscapeCsv(p.CustomerEmail),
                EscapeCsv(p.FacebookName ?? ""),
                p.LoyaltyTier,
                p.IsNewCustomerThisMonth ? "New Player" : "Returning Player",
                p.TotalBookings,
                p.TotalHours.ToString("F1"),
                p.TotalSpend.ToString("F2"),
                EscapeCsv(p.PreferredCourt),
                EscapeCsv(p.PreferredTimeSlot),
                p.FirstBookingDate.ToString("yyyy-MM-dd"),
                p.LatestBookingDate.ToString("yyyy-MM-dd")
            ));
        }
        sb.AppendLine();

        // 2. At-Risk Players Directory
        sb.AppendLine("## Inactive / At-Risk Players (Win-Back List)");
        sb.AppendLine("Customer Name,Phone,Email,Prior Bookings,Lifetime Spend (PHP),Last Active Date,Days Inactive");

        foreach (var a in report.AtRiskCustomers)
        {
            sb.AppendLine(string.Join(",",
                EscapeCsv(a.CustomerName),
                EscapeCsv(a.CustomerPhone),
                EscapeCsv(a.CustomerEmail),
                a.LifetimeBookings,
                a.LifetimeSpend.ToString("F2"),
                a.LastActiveDate.ToString("yyyy-MM-dd"),
                a.DaysSinceLastBooking
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
