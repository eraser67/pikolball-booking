using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public interface IReportService
{
    /// <summary>
    /// Generates a monthly summary report with KPIs, daily trends, court utilization,
    /// payment breakdown, and booking ledger strictly isolated to the specified tenant.
    /// </summary>
    Task<MonthlyBookingReport> GetMonthlyReportAsync(int organizationId, int year, int month);

    /// <summary>
    /// Generates a CSV export of the monthly booking ledger for the specified tenant.
    /// </summary>
    Task<byte[]> ExportMonthlyReportCsvAsync(int organizationId, int year, int month);

    /// <summary>
    /// Generates a Court Utilization & Peak Hours report with hourly heatmaps,
    /// day-of-week trends, RevPAH, and capacity metrics strictly isolated to the specified tenant.
    /// </summary>
    Task<CourtUtilizationReport> GetCourtUtilizationReportAsync(int organizationId, int year, int month);

    /// <summary>
    /// Generates a CSV export of hourly utilization and day-of-week breakdown for the specified tenant.
    /// </summary>
    Task<byte[]> ExportCourtUtilizationCsvAsync(int organizationId, int year, int month);

    /// <summary>
    /// Generates an Attendance, Check-In & No-Show report with check-in rates,
    /// no-show frequency, punctuality, method distribution, and repeat no-show risk.
    /// </summary>
    Task<AttendanceReport> GetAttendanceReportAsync(int organizationId, int year, int month);

    /// <summary>
    /// Generates a CSV export of the attendance and check-in audit ledger for the specified tenant.
    /// </summary>
    Task<byte[]> ExportAttendanceReportCsvAsync(int organizationId, int year, int month);

    /// <summary>
    /// Generates a Customer & Repeat Player Insights report with retention rates,
    /// VIP player rankings, preferred courts/times, and at-risk player detection.
    /// </summary>
    Task<CustomerInsightsReport> GetCustomerInsightsReportAsync(int organizationId, int year, int month);

    /// <summary>
    /// Generates a CSV export of customer contact and retention directory for marketing and loyalty campaigns.
    /// </summary>
    Task<byte[]> ExportCustomerDirectoryCsvAsync(int organizationId, int year, int month);
}

public class MonthlyBookingReport
{
    public int OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName => new DateTime(Year, Month, 1).ToString("MMMM yyyy");

    // Key Executive Metrics (KPIs)
    public int TotalBookingsCount { get; set; }
    public int ConfirmedCount { get; set; }
    public int CompletedCount { get; set; }
    public int PendingCount { get; set; }
    public int CancelledCount { get; set; }
    public int NoShowCount { get; set; }
    public decimal ConversionRate => TotalBookingsCount > 0 
        ? Math.Round((decimal)(ConfirmedCount + CompletedCount) / TotalBookingsCount * 100m, 1) 
        : 0m;

    public decimal TotalRevenueCollected { get; set; } // Confirmed & Completed
    public decimal PendingRevenue { get; set; } // Pending payments / holds
    public decimal LostCancelledRevenue { get; set; } // Cancelled / expired holds
    public decimal AverageBookingValue => (ConfirmedCount + CompletedCount) > 0 
        ? Math.Round(TotalRevenueCollected / (ConfirmedCount + CompletedCount), 2) 
        : 0m;
    public decimal TotalBookedHours { get; set; }

    // Breakdown By Day
    public List<DailyReportItem> DailyStats { get; set; } = new();

    // Breakdown By Court
    public List<CourtReportItem> CourtStats { get; set; } = new();

    // Breakdown By Payment Method
    public List<PaymentMethodReportItem> PaymentMethodStats { get; set; } = new();

    // Ledger of individual bookings in the month
    public List<BookingReportItem> Bookings { get; set; } = new();
}

public class DailyReportItem
{
    public DateOnly Date { get; set; }
    public int DayNumber => Date.Day;
    public string DayName => Date.ToString("ddd");
    public int TotalBookings { get; set; }
    public int ConfirmedOrCompleted { get; set; }
    public decimal Revenue { get; set; }
}

public class CourtReportItem
{
    public int CourtId { get; set; }
    public string CourtName { get; set; } = string.Empty;
    public int TotalBookings { get; set; }
    public decimal TotalHours { get; set; }
    public decimal TotalRevenue { get; set; }
}

public class PaymentMethodReportItem
{
    public string MethodName { get; set; } = string.Empty;
    public int TransactionCount { get; set; }
    public decimal TotalAmount { get; set; }
}

public class BookingReportItem
{
    public int BookingId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public DateOnly BookingDate { get; set; }
    public string TimeRange { get; set; } = string.Empty;
    public string CourtName { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public BookingStatus Status { get; set; }
    public string? PaymentMethod { get; set; }
    public string? PaymentReference { get; set; }
    public PaymentStatus? PaymentStatus { get; set; }
    public bool CheckedIn { get; set; }
    public bool IsNoShow { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CourtUtilizationReport
{
    public int OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName => new DateTime(Year, Month, 1).ToString("MMMM yyyy");
    public int DaysInMonth => DateTime.DaysInMonth(Year, Month);

    // Facility Capacity & Summary
    public int ActiveCourtsCount { get; set; }
    public decimal DailyOperatingHours { get; set; }
    public decimal TotalAvailableCapacityHours { get; set; }
    public decimal TotalBookedHours { get; set; }
    public decimal OverallUtilizationRate => TotalAvailableCapacityHours > 0
        ? Math.Round(TotalBookedHours / TotalAvailableCapacityHours * 100m, 1)
        : 0m;
    public decimal TotalRevenue { get; set; }
    public decimal RevPAH => TotalAvailableCapacityHours > 0
        ? Math.Round(TotalRevenue / TotalAvailableCapacityHours, 2)
        : 0m;

    // Peak & Off-Peak Identifiers
    public string PeakHourWindow { get; set; } = string.Empty;
    public decimal PeakHourUtilization { get; set; }
    public string OffPeakHourWindow { get; set; } = string.Empty;
    public decimal OffPeakHourUtilization { get; set; }
    public string BusiestDayOfWeek { get; set; } = string.Empty;
    public string QuietestDayOfWeek { get; set; } = string.Empty;

    // Weekday vs Weekend Comparison
    public WeekdayWeekendComparison WeekdayVsWeekend { get; set; } = new();

    // Hourly Breakdown (e.g. 06:00 to 23:00)
    public List<HourlyUtilizationItem> HourlyStats { get; set; } = new();

    // Day of the Week Distribution (Monday - Sunday)
    public List<DayOfWeekUtilizationItem> DayOfWeekStats { get; set; } = new();

    // Per-Court Utilization Table
    public List<CourtUtilizationDetailItem> CourtDetails { get; set; } = new();

    // Strategic Recommendations
    public List<string> StrategicRecommendations { get; set; } = new();
}

public class HourlyUtilizationItem
{
    public int Hour { get; set; }
    public string TimeLabel => $"{DateTime.Today.AddHours(Hour):h:mm tt} - {DateTime.Today.AddHours(Hour + 1):h:mm tt}";
    public int BookingsCount { get; set; }
    public decimal BookedHours { get; set; }
    public decimal Revenue { get; set; }
    public decimal CapacityHours { get; set; }
    public decimal UtilizationRate => CapacityHours > 0 
        ? Math.Round(BookedHours / CapacityHours * 100m, 1) 
        : 0m;
    public string DemandLevel => UtilizationRate >= 65m ? "Peak" : UtilizationRate >= 35m ? "Moderate" : "Off-Peak";
}

public class DayOfWeekUtilizationItem
{
    public DayOfWeek DayOfWeek { get; set; }
    public string DayName => DayOfWeek.ToString();
    public int OccurrenceCount { get; set; }
    public int TotalBookings { get; set; }
    public decimal BookedHours { get; set; }
    public decimal Revenue { get; set; }
    public decimal CapacityHours { get; set; }
    public decimal UtilizationRate => CapacityHours > 0 
        ? Math.Round(BookedHours / CapacityHours * 100m, 1) 
        : 0m;
}

public class WeekdayWeekendComparison
{
    public decimal WeekdayBookedHours { get; set; }
    public decimal WeekdayRevenue { get; set; }
    public decimal WeekdayCapacityHours { get; set; }
    public decimal WeekdayUtilizationRate => WeekdayCapacityHours > 0 
        ? Math.Round(WeekdayBookedHours / WeekdayCapacityHours * 100m, 1) 
        : 0m;

    public decimal WeekendBookedHours { get; set; }
    public decimal WeekendRevenue { get; set; }
    public decimal WeekendCapacityHours { get; set; }
    public decimal WeekendUtilizationRate => WeekendCapacityHours > 0 
        ? Math.Round(WeekendBookedHours / WeekendCapacityHours * 100m, 1) 
        : 0m;
}

public class CourtUtilizationDetailItem
{
    public int CourtId { get; set; }
    public string CourtName { get; set; } = string.Empty;
    public CourtStatus Status { get; set; }
    public int TotalBookings { get; set; }
    public decimal BookedHours { get; set; }
    public decimal AvailableCapacityHours { get; set; }
    public decimal UtilizationRate => AvailableCapacityHours > 0
        ? Math.Round(BookedHours / AvailableCapacityHours * 100m, 1)
        : 0m;
    public decimal Revenue { get; set; }
    public decimal RevPAH => AvailableCapacityHours > 0
        ? Math.Round(Revenue / AvailableCapacityHours, 2)
        : 0m;
}

public class AttendanceReport
{
    public int OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName => new DateTime(Year, Month, 1).ToString("MMMM yyyy");

    // Executive Metrics
    public int TotalEligibleBookings { get; set; }
    public int CheckedInCount { get; set; }
    public int NoShowCount { get; set; }
    public int UnmarkedCount { get; set; }

    public decimal CheckInRate => TotalEligibleBookings > 0 
        ? Math.Round((decimal)CheckedInCount / TotalEligibleBookings * 100m, 1) 
        : 0m;

    public decimal NoShowRate => TotalEligibleBookings > 0 
        ? Math.Round((decimal)NoShowCount / TotalEligibleBookings * 100m, 1) 
        : 0m;

    public decimal NoShowRevenueLoss { get; set; }
    public decimal RealizedCheckedInRevenue { get; set; }

    public double? AveragePunctualityMinutes { get; set; }

    // Breakdown By Check-In Method
    public List<CheckInMethodReportItem> MethodStats { get; set; } = new();

    // Breakdown By Day of Week
    public List<DayOfWeekAttendanceItem> DayOfWeekStats { get; set; } = new();

    // Repeat No-Show / Customer Reliability
    public List<CustomerReliabilityItem> CustomerReliabilityStats { get; set; } = new();

    // Ledger of Attendance records
    public List<AttendanceRecordItem> Records { get; set; } = new();

    // Automated Insights
    public List<string> StrategicInsights { get; set; } = new();
}

public class CheckInMethodReportItem
{
    public string MethodName { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal SharePercentage { get; set; }
}

public class DayOfWeekAttendanceItem
{
    public DayOfWeek DayOfWeek { get; set; }
    public string DayName => DayOfWeek.ToString();
    public int TotalBookings { get; set; }
    public int CheckedInCount { get; set; }
    public int NoShowCount { get; set; }
    public decimal CheckInRate => TotalBookings > 0 ? Math.Round((decimal)CheckedInCount / TotalBookings * 100m, 1) : 0m;
    public decimal NoShowRate => TotalBookings > 0 ? Math.Round((decimal)NoShowCount / TotalBookings * 100m, 1) : 0m;
}

public class CustomerReliabilityItem
{
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public int TotalBookings { get; set; }
    public int CheckedInCount { get; set; }
    public int NoShowCount { get; set; }
    public decimal ReliabilityRate => TotalBookings > 0 ? Math.Round((decimal)CheckedInCount / TotalBookings * 100m, 1) : 0m;
    public string RiskLevel => NoShowCount >= 3 || (TotalBookings >= 3 && ReliabilityRate < 50m) ? "High" :
                               NoShowCount >= 1 ? "Medium" : "Reliable";
}

public class AttendanceRecordItem
{
    public int BookingId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public DateOnly BookingDate { get; set; }
    public string TimeRange { get; set; } = string.Empty;
    public string CourtName { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public BookingStatus BookingStatus { get; set; }
    public bool CheckedIn { get; set; }
    public DateTime? CheckedInAtLocal { get; set; }
    public CheckInMethod? CheckInMethod { get; set; }
    public string? CheckedInByUserId { get; set; }
    public bool IsNoShow { get; set; }
    public int? PunctualityMinutes { get; set; }
}

public class CustomerInsightsReport
{
    public int OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName => new DateTime(Year, Month, 1).ToString("MMMM yyyy");

    // Executive Customer Metrics
    public int TotalUniqueCustomersInMonth { get; set; }
    public int NewCustomersInMonth { get; set; }
    public int ReturningCustomersInMonth { get; set; }
    public decimal RepeatCustomerRate => TotalUniqueCustomersInMonth > 0
        ? Math.Round((decimal)ReturningCustomersInMonth / TotalUniqueCustomersInMonth * 100m, 1)
        : 0m;

    public decimal AverageSpendPerCustomer => TotalUniqueCustomersInMonth > 0
        ? Math.Round(TotalRevenue / TotalUniqueCustomersInMonth, 2)
        : 0m;

    public decimal TotalRevenue { get; set; }
    public decimal TopTenCustomerRevenueShare { get; set; }

    // Customer Loyalty Tiers
    public int VipPlayersCount { get; set; }
    public int RegularPlayersCount { get; set; }
    public int OccasionalPlayersCount { get; set; }

    // Top VIP Players Ranking
    public List<VipPlayerItem> TopPlayers { get; set; } = new();

    // At-Risk / Inactive Customers
    public List<InactiveCustomerItem> AtRiskCustomers { get; set; } = new();

    // Automated Marketing & Retention Recommendations
    public List<string> StrategicRecommendations { get; set; } = new();
}

public class VipPlayerItem
{
    public int Rank { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string? FacebookName { get; set; }
    public int TotalBookings { get; set; }
    public decimal TotalHours { get; set; }
    public decimal TotalSpend { get; set; }
    public string PreferredCourt { get; set; } = string.Empty;
    public string PreferredTimeSlot { get; set; } = string.Empty;
    public DateOnly FirstBookingDate { get; set; }
    public DateOnly LatestBookingDate { get; set; }
    public bool IsNewCustomerThisMonth { get; set; }
    public string LoyaltyTier => TotalBookings >= 5 ? "VIP Tier" : TotalBookings >= 2 ? "Regular" : "Occasional";
}

public class InactiveCustomerItem
{
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public int LifetimeBookings { get; set; }
    public decimal LifetimeSpend { get; set; }
    public DateOnly LastActiveDate { get; set; }
    public int DaysSinceLastBooking { get; set; }
}
