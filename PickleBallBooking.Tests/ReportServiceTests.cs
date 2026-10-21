using System.Text;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using Xunit;

namespace PickleBallBooking.Tests;

public class ReportServiceTests
{
    private const int Org1 = 1;
    private const int Org2 = 2;

    private static ApplicationDbContext CreateContext(string dbName)
    {
        return TestDbContextFactory.CreateInMemory(dbName, Org1);
    }

    [Fact]
    public async Task GetMonthlyReportAsync_CalculatesAccurateRevenueAndCounts()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);

        var court1 = new Court { Id = 1, Name = "Court Alpha", Status = CourtStatus.Active, OrganizationId = Org1 };
        context.Courts.Add(court1);

        // Booking 1: Confirmed, ₱500, Oct 10
        var b1 = new Booking
        {
            Id = 1,
            OrganizationId = Org1,
            BookingReference = "PB-OCT01",
            CustomerName = "Alice",
            CustomerPhone = "09170000001",
            CustomerEmail = "alice@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 10),
            StartTime = new TimeSpan(9, 0, 0),
            EndTime = new TimeSpan(10, 0, 0),
            Price = 500m,
            BookingStatus = BookingStatus.Confirmed
        };

        // Booking 2: Completed, ₱700, Oct 15
        var b2 = new Booking
        {
            Id = 2,
            OrganizationId = Org1,
            BookingReference = "PB-OCT02",
            CustomerName = "Bob",
            CustomerPhone = "09170000002",
            CustomerEmail = "bob@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 15),
            StartTime = new TimeSpan(14, 0, 0),
            EndTime = new TimeSpan(16, 0, 0),
            Price = 700m,
            BookingStatus = BookingStatus.Completed
        };

        // Booking 3: Cancelled, ₱400, Oct 20
        var b3 = new Booking
        {
            Id = 3,
            OrganizationId = Org1,
            BookingReference = "PB-OCT03",
            CustomerName = "Charlie",
            CustomerPhone = "09170000003",
            CustomerEmail = "charlie@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 20),
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(19, 0, 0),
            Price = 400m,
            BookingStatus = BookingStatus.Cancelled
        };

        // Booking 4: Pending hold, ₱300, Oct 25
        var b4 = new Booking
        {
            Id = 4,
            OrganizationId = Org1,
            BookingReference = "PB-OCT04",
            CustomerName = "David",
            CustomerPhone = "09170000004",
            CustomerEmail = "david@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 25),
            StartTime = new TimeSpan(19, 0, 0),
            EndTime = new TimeSpan(20, 0, 0),
            Price = 300m,
            BookingStatus = BookingStatus.Pending
        };

        context.Bookings.AddRange(b1, b2, b3, b4);
        await context.SaveChangesAsync();

        var service = new ReportService(context);

        // Act
        var report = await service.GetMonthlyReportAsync(Org1, 2026, 10);

        // Assert
        Assert.Equal(Org1, report.OrganizationId);
        Assert.Equal(2026, report.Year);
        Assert.Equal(10, report.Month);
        Assert.Equal(4, report.TotalBookingsCount);
        Assert.Equal(1, report.ConfirmedCount);
        Assert.Equal(1, report.CompletedCount);
        Assert.Equal(1, report.CancelledCount);
        Assert.Equal(1, report.PendingCount);

        // Revenue checks
        Assert.Equal(1200m, report.TotalRevenueCollected); // 500 + 700
        Assert.Equal(300m, report.PendingRevenue);
        Assert.Equal(400m, report.LostCancelledRevenue);
        Assert.Equal(600m, report.AverageBookingValue); // 1200 / 2
        Assert.Equal(50m, report.ConversionRate); // 2 out of 4 = 50%

        // Court checks
        Assert.Single(report.CourtStats);
        Assert.Equal("Court Alpha", report.CourtStats[0].CourtName);
        Assert.Equal(1200m, report.CourtStats[0].TotalRevenue);

        // Ledger checks
        Assert.Equal(4, report.Bookings.Count);
    }

    [Fact]
    public async Task GetMonthlyReportAsync_EnforcesStrictTenantIsolation()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);
        context.SuppressTenantWriteGuard = true;

        var court1 = new Court { Id = 1, Name = "Court Alpha", Status = CourtStatus.Active, OrganizationId = Org1 };
        var court2 = new Court { Id = 2, Name = "Court Beta", Status = CourtStatus.Active, OrganizationId = Org2 };
        context.Courts.AddRange(court1, court2);

        // Tenant 1 Booking
        var t1Booking = new Booking
        {
            Id = 101,
            OrganizationId = Org1,
            BookingReference = "PB-TENANT1",
            CustomerName = "T1 Customer",
            CustomerPhone = "09170000001",
            CustomerEmail = "t1@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 5),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(11, 0, 0),
            Price = 800m,
            BookingStatus = BookingStatus.Confirmed
        };

        // Tenant 2 Booking (Should NEVER leak into Tenant 1's report)
        var t2Booking = new Booking
        {
            Id = 201,
            OrganizationId = Org2,
            BookingReference = "PB-TENANT2",
            CustomerName = "T2 Customer",
            CustomerPhone = "09170000002",
            CustomerEmail = "t2@test.com",
            CourtId = 2,
            BookingDate = new DateOnly(2026, 10, 5),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(11, 0, 0),
            Price = 1500m,
            BookingStatus = BookingStatus.Confirmed
        };

        context.Bookings.AddRange(t1Booking, t2Booking);
        await context.SaveChangesAsync();

        var service = new ReportService(context);

        // Act: Request report for Org1 using Org1's context
        var reportOrg1 = await service.GetMonthlyReportAsync(Org1, 2026, 10);

        // Assert: Org1 report only contains Org1 data
        Assert.Equal(1, reportOrg1.TotalBookingsCount);
        Assert.Equal(800m, reportOrg1.TotalRevenueCollected);
        Assert.Single(reportOrg1.Bookings);
        Assert.Equal("PB-TENANT1", reportOrg1.Bookings[0].BookingReference);
        Assert.DoesNotContain(reportOrg1.Bookings, b => b.BookingReference == "PB-TENANT2");

        // Act & Assert: For Org2, create a context for Org2
        await using var context2 = TestDbContextFactory.CreateInMemory(dbName, Org2);
        var service2 = new ReportService(context2);
        var reportOrg2 = await service2.GetMonthlyReportAsync(Org2, 2026, 10);

        Assert.Equal(1, reportOrg2.TotalBookingsCount);
        Assert.Equal(1500m, reportOrg2.TotalRevenueCollected);
        Assert.Single(reportOrg2.Bookings);
        Assert.Equal("PB-TENANT2", reportOrg2.Bookings[0].BookingReference);
        Assert.DoesNotContain(reportOrg2.Bookings, b => b.BookingReference == "PB-TENANT1");
    }

    [Fact]
    public async Task GetMonthlyReportAsync_ExcludesBookingsOutsideSpecifiedMonth()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);

        var court = new Court { Id = 1, Name = "Court Alpha", Status = CourtStatus.Active, OrganizationId = Org1 };
        context.Courts.Add(court);

        // Sep 30 (previous month)
        var sepBooking = new Booking
        {
            Id = 1,
            OrganizationId = Org1,
            BookingReference = "PB-SEP",
            CustomerName = "Sep Player",
            CustomerPhone = "09170000001",
            CustomerEmail = "sep@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 9, 30),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(11, 0, 0),
            Price = 500m,
            BookingStatus = BookingStatus.Confirmed
        };

        // Oct 1 (target month)
        var octBooking = new Booking
        {
            Id = 2,
            OrganizationId = Org1,
            BookingReference = "PB-OCT",
            CustomerName = "Oct Player",
            CustomerPhone = "09170000002",
            CustomerEmail = "oct@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 1),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(11, 0, 0),
            Price = 600m,
            BookingStatus = BookingStatus.Confirmed
        };

        // Nov 1 (next month)
        var novBooking = new Booking
        {
            Id = 3,
            OrganizationId = Org1,
            BookingReference = "PB-NOV",
            CustomerName = "Nov Player",
            CustomerPhone = "09170000003",
            CustomerEmail = "nov@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 11, 1),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(11, 0, 0),
            Price = 700m,
            BookingStatus = BookingStatus.Confirmed
        };

        context.Bookings.AddRange(sepBooking, octBooking, novBooking);
        await context.SaveChangesAsync();

        var service = new ReportService(context);

        // Act
        var report = await service.GetMonthlyReportAsync(Org1, 2026, 10);

        // Assert
        Assert.Single(report.Bookings);
        Assert.Equal("PB-OCT", report.Bookings[0].BookingReference);
        Assert.Equal(600m, report.TotalRevenueCollected);
    }

    [Fact]
    public async Task ExportMonthlyReportCsvAsync_GeneratesProperlyFormattedCsv()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);

        var court = new Court { Id = 1, Name = "Court Alpha", Status = CourtStatus.Active, OrganizationId = Org1 };
        context.Courts.Add(court);

        var booking = new Booking
        {
            Id = 1,
            OrganizationId = Org1,
            BookingReference = "PB-CSV01",
            CustomerName = "Delacruz, Juan", // contains comma
            CustomerPhone = "09171234567",
            CustomerEmail = "juan@example.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 12),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(11, 0, 0),
            Price = 450m,
            BookingStatus = BookingStatus.Confirmed
        };

        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        var service = new ReportService(context);

        // Act
        var csvBytes = await service.ExportMonthlyReportCsvAsync(Org1, 2026, 10);
        var csvText = Encoding.UTF8.GetString(csvBytes);

        // Assert
        Assert.NotEmpty(csvBytes);
        Assert.Contains("Monthly Booking & Revenue Report", csvText);
        Assert.Contains("PB-CSV01", csvText);
        Assert.Contains("\"Delacruz, Juan\"", csvText); // properly quoted
        Assert.Contains("450.00", csvText);
        Assert.Contains("Court Alpha", csvText);
    }

    [Fact]
    public async Task GetCourtUtilizationReportAsync_CalculatesAccurateUtilizationAndPeakHours()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);

        var court1 = new Court { Id = 1, Name = "Court Alpha", Status = CourtStatus.Active, OrganizationId = Org1 };
        context.Courts.Add(court1);

        // Master slot: 18:00 - 19:00 (1 hour)
        var slot1 = new TimeSlot { Id = 1, StartTime = new TimeSpan(18, 0, 0), EndTime = new TimeSpan(19, 0, 0), Status = TimeSlotStatus.Active };
        // Master slot: 19:00 - 20:00 (1 hour)
        var slot2 = new TimeSlot { Id = 2, StartTime = new TimeSpan(19, 0, 0), EndTime = new TimeSpan(20, 0, 0), Status = TimeSlotStatus.Active };
        context.TimeSlots.AddRange(slot1, slot2);

        // Booking 1: Oct 9 (Friday), 18:00 - 20:00 (2 hrs), ₱1,000, Confirmed
        var b1 = new Booking
        {
            Id = 1,
            OrganizationId = Org1,
            BookingReference = "PB-UTIL01",
            CustomerName = "John",
            CustomerPhone = "09170000001",
            CustomerEmail = "john@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 9), // Friday
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(20, 0, 0),
            DurationHours = 2m,
            Price = 1000m,
            BookingStatus = BookingStatus.Confirmed
        };

        // Booking 2: Oct 10 (Saturday), 18:00 - 19:00 (1 hr), ₱500, Completed
        var b2 = new Booking
        {
            Id = 2,
            OrganizationId = Org1,
            BookingReference = "PB-UTIL02",
            CustomerName = "Jane",
            CustomerPhone = "09170000002",
            CustomerEmail = "jane@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 10), // Saturday (Weekend)
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(19, 0, 0),
            DurationHours = 1m,
            Price = 500m,
            BookingStatus = BookingStatus.Completed
        };

        context.Bookings.AddRange(b1, b2);
        await context.SaveChangesAsync();

        var service = new ReportService(context);

        // Act
        var report = await service.GetCourtUtilizationReportAsync(Org1, 2026, 10);

        // Assert
        Assert.Equal(Org1, report.OrganizationId);
        Assert.Equal(1, report.ActiveCourtsCount);
        Assert.Equal(2m, report.DailyOperatingHours); // 2 hours configured
        Assert.Equal(31 * 2m, report.TotalAvailableCapacityHours); // 62 hours capacity
        Assert.Equal(3m, report.TotalBookedHours); // 2 + 1 = 3 hrs
        Assert.Equal(1500m, report.TotalRevenue);

        // Peak Hour: hour 18 (6:00 PM) has 2 bookings (b1 overlap 1h + b2 overlap 1h = 2h)
        var hour18 = report.HourlyStats.FirstOrDefault(h => h.Hour == 18);
        Assert.NotNull(hour18);
        Assert.Equal(2, hour18.BookingsCount);
        Assert.Equal(2m, hour18.BookedHours);
        Assert.Equal(1000m, hour18.Revenue); // 500 from b1 + 500 from b2

        // Hour 19 (7:00 PM) has 1 booking (b1 overlap 1h = 1h)
        var hour19 = report.HourlyStats.FirstOrDefault(h => h.Hour == 19);
        Assert.NotNull(hour19);
        Assert.Equal(1, hour19.BookingsCount);
        Assert.Equal(1m, hour19.BookedHours);
        Assert.Equal(500m, hour19.Revenue);

        // Weekday vs Weekend checks
        Assert.Equal(2m, report.WeekdayVsWeekend.WeekdayBookedHours);
        Assert.Equal(1000m, report.WeekdayVsWeekend.WeekdayRevenue);
        Assert.Equal(1m, report.WeekdayVsWeekend.WeekendBookedHours);
        Assert.Equal(500m, report.WeekdayVsWeekend.WeekendRevenue);

        // Court details check
        Assert.Single(report.CourtDetails);
        Assert.Equal("Court Alpha", report.CourtDetails[0].CourtName);
        Assert.Equal(3m, report.CourtDetails[0].BookedHours);
        Assert.Equal(1500m, report.CourtDetails[0].Revenue);
        Assert.True(report.CourtDetails[0].RevPAH > 0m);

        // Hourly slots check: exactly 18 slots from 8 AM to 2 AM
        Assert.Equal(18, report.HourlyStats.Count);
        Assert.Equal(8, report.HourlyStats[0].Hour);
        Assert.Equal("8:00 AM - 9:00 AM", report.HourlyStats[0].TimeLabel);
        Assert.Equal(23, report.HourlyStats[15].Hour);
        Assert.Equal("11:00 PM - 12:00 AM", report.HourlyStats[15].TimeLabel);
        Assert.Equal(0, report.HourlyStats[16].Hour);
        Assert.Equal("12:00 AM - 1:00 AM", report.HourlyStats[16].TimeLabel);
        Assert.Equal(1, report.HourlyStats[17].Hour);
        Assert.Equal("1:00 AM - 2:00 AM", report.HourlyStats[17].TimeLabel);

        // Strategic recommendations generated
        Assert.NotEmpty(report.StrategicRecommendations);
    }

    [Fact]
    public async Task GetCourtUtilizationReportAsync_OperatingHours8AMTo2AM_AccuratelyDistributesMidnightBookings()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);
        context.SuppressTenantWriteGuard = true;

        var court = new Court { Id = 1, Name = "Court Night", Status = CourtStatus.Active, OrganizationId = Org1 };
        context.Courts.Add(court);

        // Booking 1: 11:00 PM – 12:00 AM (Hour 23), ₱600
        var b1 = new Booking
        {
            Id = 1,
            OrganizationId = Org1,
            BookingReference = "PB-NIGHT01",
            CustomerName = "Night Owl 1",
            CustomerPhone = "09170000001",
            CustomerEmail = "owl1@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 15),
            StartTime = new TimeSpan(23, 0, 0),
            EndTime = TimeSpan.Zero, // 00:00:00 midnight
            DurationHours = 1m,
            Price = 600m,
            BookingStatus = BookingStatus.Confirmed
        };

        // Booking 2: 12:00 AM – 1:00 AM (Hour 0), ₱650
        var b2 = new Booking
        {
            Id = 2,
            OrganizationId = Org1,
            BookingReference = "PB-NIGHT02",
            CustomerName = "Night Owl 2",
            CustomerPhone = "09170000002",
            CustomerEmail = "owl2@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 15),
            StartTime = new TimeSpan(0, 0, 0),
            EndTime = new TimeSpan(1, 0, 0),
            DurationHours = 1m,
            Price = 650m,
            BookingStatus = BookingStatus.Confirmed
        };

        // Booking 3: 1:00 AM – 2:00 AM (Hour 1), ₱700
        var b3 = new Booking
        {
            Id = 3,
            OrganizationId = Org1,
            BookingReference = "PB-NIGHT03",
            CustomerName = "Night Owl 3",
            CustomerPhone = "09170000003",
            CustomerEmail = "owl3@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 15),
            StartTime = new TimeSpan(1, 0, 0),
            EndTime = new TimeSpan(2, 0, 0),
            DurationHours = 1m,
            Price = 700m,
            BookingStatus = BookingStatus.Confirmed
        };

        context.Bookings.AddRange(b1, b2, b3);
        await context.SaveChangesAsync();

        var service = new ReportService(context);

        // Act
        var report = await service.GetCourtUtilizationReportAsync(Org1, 2026, 10);

        // Assert
        Assert.Equal(18, report.HourlyStats.Count);

        var slot23 = report.HourlyStats.First(h => h.Hour == 23);
        Assert.Equal("11:00 PM - 12:00 AM", slot23.TimeLabel);
        Assert.Equal(1, slot23.BookingsCount);
        Assert.Equal(1m, slot23.BookedHours);
        Assert.Equal(600m, slot23.Revenue);

        var slot0 = report.HourlyStats.First(h => h.Hour == 0);
        Assert.Equal("12:00 AM - 1:00 AM", slot0.TimeLabel);
        Assert.Equal(1, slot0.BookingsCount);
        Assert.Equal(1m, slot0.BookedHours);
        Assert.Equal(650m, slot0.Revenue);

        var slot1 = report.HourlyStats.First(h => h.Hour == 1);
        Assert.Equal("1:00 AM - 2:00 AM", slot1.TimeLabel);
        Assert.Equal(1, slot1.BookingsCount);
        Assert.Equal(1m, slot1.BookedHours);
        Assert.Equal(700m, slot1.Revenue);
    }

    [Fact]
    public async Task GetCourtUtilizationReportAsync_EnforcesStrictTenantIsolation()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);
        context.SuppressTenantWriteGuard = true;

        var court1 = new Court { Id = 1, Name = "Org1 Court", Status = CourtStatus.Active, OrganizationId = Org1 };
        var court2 = new Court { Id = 2, Name = "Org2 Court", Status = CourtStatus.Active, OrganizationId = Org2 };
        context.Courts.AddRange(court1, court2);

        // Org 1 Booking (5 hours, ₱2,500)
        var org1Booking = new Booking
        {
            Id = 1,
            OrganizationId = Org1,
            BookingReference = "PB-ORG1-UTIL",
            CustomerName = "Org1 Customer",
            CustomerPhone = "09170000001",
            CustomerEmail = "org1@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 5),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(15, 0, 0),
            DurationHours = 5m,
            Price = 2500m,
            BookingStatus = BookingStatus.Confirmed
        };

        // Org 2 Booking (2 hours, ₱1,200) - Should NEVER leak into Org 1
        var org2Booking = new Booking
        {
            Id = 2,
            OrganizationId = Org2,
            BookingReference = "PB-ORG2-UTIL",
            CustomerName = "Org2 Customer",
            CustomerPhone = "09170000002",
            CustomerEmail = "org2@test.com",
            CourtId = 2,
            BookingDate = new DateOnly(2026, 10, 5),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(12, 0, 0),
            DurationHours = 2m,
            Price = 1200m,
            BookingStatus = BookingStatus.Confirmed
        };

        context.Bookings.AddRange(org1Booking, org2Booking);
        await context.SaveChangesAsync();

        var service = new ReportService(context);

        // Act: Fetch for Org 1
        var reportOrg1 = await service.GetCourtUtilizationReportAsync(Org1, 2026, 10);

        // Assert: Org 1 report contains ONLY Org 1 data
        Assert.Equal(Org1, reportOrg1.OrganizationId);
        Assert.Equal(1, reportOrg1.ActiveCourtsCount);
        Assert.Single(reportOrg1.CourtDetails);
        Assert.Equal("Org1 Court", reportOrg1.CourtDetails[0].CourtName);
        Assert.Equal(5m, reportOrg1.TotalBookedHours);
        Assert.Equal(2500m, reportOrg1.TotalRevenue);

        // Act & Assert: Fetch for Org 2
        await using var context2 = TestDbContextFactory.CreateInMemory(dbName, Org2);
        var service2 = new ReportService(context2);
        var reportOrg2 = await service2.GetCourtUtilizationReportAsync(Org2, 2026, 10);

        Assert.Equal(Org2, reportOrg2.OrganizationId);
        Assert.Equal(1, reportOrg2.ActiveCourtsCount);
        Assert.Single(reportOrg2.CourtDetails);
        Assert.Equal("Org2 Court", reportOrg2.CourtDetails[0].CourtName);
        Assert.Equal(2m, reportOrg2.TotalBookedHours);
        Assert.Equal(1200m, reportOrg2.TotalRevenue);
    }

    [Fact]
    public async Task ExportCourtUtilizationCsvAsync_GeneratesProperlyFormattedCsv()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);

        var court = new Court { Id = 1, Name = "Main Arena Court", Status = CourtStatus.Active, OrganizationId = Org1 };
        context.Courts.Add(court);

        var booking = new Booking
        {
            Id = 1,
            OrganizationId = Org1,
            BookingReference = "PB-CSV-UTIL",
            CustomerName = "Carlos, Juan",
            CustomerPhone = "09171112222",
            CustomerEmail = "carlos@example.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 14),
            StartTime = new TimeSpan(17, 0, 0),
            EndTime = new TimeSpan(19, 0, 0),
            DurationHours = 2m,
            Price = 900m,
            BookingStatus = BookingStatus.Confirmed
        };

        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        var service = new ReportService(context);

        // Act
        var csvBytes = await service.ExportCourtUtilizationCsvAsync(Org1, 2026, 10);
        var csvText = Encoding.UTF8.GetString(csvBytes);

        // Assert
        Assert.NotEmpty(csvBytes);
        Assert.Contains("Court Utilization & Peak Hours Report", csvText);
        Assert.Contains("Main Arena Court", csvText);
        Assert.Contains("Hourly Distribution & Peak Demand", csvText);
        Assert.Contains("Day of the Week Performance", csvText);
        Assert.Contains("Per-Court Utilization & RevPAH", csvText);
    }

    [Fact]
    public async Task GetAttendanceReportAsync_CalculatesAccurateAttendanceAndNoShows()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);

        var court = new Court { Id = 1, Name = "Center Court", Status = CourtStatus.Active, OrganizationId = Org1 };
        context.Courts.Add(court);

        // Booking 1: Oct 12, 10:00 - Checked in 10 mins early via QR Scan (Price ₱500)
        var scheduledStart1 = new DateTime(2026, 10, 12, 10, 0, 0); // 10:00 Manila
        var checkInUtc1 = AppClock.ToUtcFromPhilippineTime(scheduledStart1.AddMinutes(-10)); // 9:50 Manila

        var b1 = new Booking
        {
            Id = 1,
            OrganizationId = Org1,
            BookingReference = "PB-ATT-01",
            CustomerName = "Carlos Player",
            CustomerPhone = "09170001111",
            CustomerEmail = "carlos@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 12),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(11, 0, 0),
            Price = 500m,
            BookingStatus = BookingStatus.Confirmed,
            CheckedInAt = checkInUtc1,
            CheckInMethod = CheckInMethod.QrScan,
            IsNoShow = false
        };

        // Booking 2: Oct 12, 11:00 - No-Show (Price ₱600)
        var b2 = new Booking
        {
            Id = 2,
            OrganizationId = Org1,
            BookingReference = "PB-ATT-02",
            CustomerName = "Carlos Player", // Same customer, repeat offender
            CustomerPhone = "09170001111",
            CustomerEmail = "carlos@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 12),
            StartTime = new TimeSpan(11, 0, 0),
            EndTime = new TimeSpan(12, 0, 0),
            Price = 600m,
            BookingStatus = BookingStatus.Confirmed,
            CheckedInAt = null,
            IsNoShow = true
        };

        // Booking 3: Oct 15, 14:00 - Unmarked / Pending Arrival (Price ₱400)
        var b3 = new Booking
        {
            Id = 3,
            OrganizationId = Org1,
            BookingReference = "PB-ATT-03",
            CustomerName = "Maria Santos",
            CustomerPhone = "09170002222",
            CustomerEmail = "maria@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 15),
            StartTime = new TimeSpan(14, 0, 0),
            EndTime = new TimeSpan(15, 0, 0),
            Price = 400m,
            BookingStatus = BookingStatus.Confirmed,
            CheckedInAt = null,
            IsNoShow = false
        };

        context.Bookings.AddRange(b1, b2, b3);
        await context.SaveChangesAsync();

        var service = new ReportService(context);

        // Act
        var report = await service.GetAttendanceReportAsync(Org1, 2026, 10);

        // Assert
        Assert.Equal(Org1, report.OrganizationId);
        Assert.Equal(3, report.TotalEligibleBookings);
        Assert.Equal(1, report.CheckedInCount);
        Assert.Equal(1, report.NoShowCount);
        Assert.Equal(1, report.UnmarkedCount);

        // Turnout rates
        Assert.Equal(33.3m, report.CheckInRate);
        Assert.Equal(33.3m, report.NoShowRate);
        Assert.Equal(600m, report.NoShowRevenueLoss);
        Assert.Equal(500m, report.RealizedCheckedInRevenue);

        // Punctuality: -10 minutes (early)
        Assert.NotNull(report.AveragePunctualityMinutes);
        Assert.Equal(-10.0, report.AveragePunctualityMinutes.Value);

        // Method Breakdown
        Assert.Single(report.MethodStats);
        Assert.Equal("QR Pass Scan", report.MethodStats[0].MethodName);
        Assert.Equal(1, report.MethodStats[0].Count);

        // Customer Reliability
        Assert.Equal(2, report.CustomerReliabilityStats.Count);
        var carlos = report.CustomerReliabilityStats.FirstOrDefault(c => c.CustomerPhone == "09170001111");
        Assert.NotNull(carlos);
        Assert.Equal(2, carlos.TotalBookings);
        Assert.Equal(1, carlos.CheckedInCount);
        Assert.Equal(1, carlos.NoShowCount);
        Assert.Equal(50.0m, carlos.ReliabilityRate);

        // Records check
        Assert.Equal(3, report.Records.Count);
        Assert.NotEmpty(report.StrategicInsights);
    }

    [Fact]
    public async Task GetAttendanceReportAsync_EnforcesStrictTenantIsolation()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);
        context.SuppressTenantWriteGuard = true;

        var court1 = new Court { Id = 1, Name = "Org1 Court", Status = CourtStatus.Active, OrganizationId = Org1 };
        var court2 = new Court { Id = 2, Name = "Org2 Court", Status = CourtStatus.Active, OrganizationId = Org2 };
        context.Courts.AddRange(court1, court2);

        // Org 1 Booking (Checked In, ₱700)
        var org1Booking = new Booking
        {
            Id = 1,
            OrganizationId = Org1,
            BookingReference = "PB-T1-ATT",
            CustomerName = "T1 Player",
            CustomerPhone = "09170000001",
            CustomerEmail = "t1@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 5),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(11, 0, 0),
            Price = 700m,
            BookingStatus = BookingStatus.Confirmed,
            CheckedInAt = DateTime.UtcNow,
            IsNoShow = false
        };

        // Org 2 Booking (No-Show, ₱900) - Must NEVER leak into Org 1
        var org2Booking = new Booking
        {
            Id = 2,
            OrganizationId = Org2,
            BookingReference = "PB-T2-ATT",
            CustomerName = "T2 Player",
            CustomerPhone = "09170000002",
            CustomerEmail = "t2@test.com",
            CourtId = 2,
            BookingDate = new DateOnly(2026, 10, 5),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(11, 0, 0),
            Price = 900m,
            BookingStatus = BookingStatus.Confirmed,
            CheckedInAt = null,
            IsNoShow = true
        };

        context.Bookings.AddRange(org1Booking, org2Booking);
        await context.SaveChangesAsync();

        var service = new ReportService(context);

        // Act: Fetch for Org 1
        var reportOrg1 = await service.GetAttendanceReportAsync(Org1, 2026, 10);

        // Assert: Org 1 report contains ONLY Org 1 data
        Assert.Equal(Org1, reportOrg1.OrganizationId);
        Assert.Equal(1, reportOrg1.TotalEligibleBookings);
        Assert.Equal(1, reportOrg1.CheckedInCount);
        Assert.Equal(0, reportOrg1.NoShowCount);
        Assert.Equal(700m, reportOrg1.RealizedCheckedInRevenue);
        Assert.Single(reportOrg1.Records);
        Assert.Equal("PB-T1-ATT", reportOrg1.Records[0].BookingReference);
        Assert.DoesNotContain(reportOrg1.Records, r => r.BookingReference == "PB-T2-ATT");

        // Act & Assert: Fetch for Org 2
        await using var context2 = TestDbContextFactory.CreateInMemory(dbName, Org2);
        var service2 = new ReportService(context2);
        var reportOrg2 = await service2.GetAttendanceReportAsync(Org2, 2026, 10);

        Assert.Equal(Org2, reportOrg2.OrganizationId);
        Assert.Equal(1, reportOrg2.TotalEligibleBookings);
        Assert.Equal(0, reportOrg2.CheckedInCount);
        Assert.Equal(1, reportOrg2.NoShowCount);
        Assert.Equal(900m, reportOrg2.NoShowRevenueLoss);
        Assert.Single(reportOrg2.Records);
        Assert.Equal("PB-T2-ATT", reportOrg2.Records[0].BookingReference);
        Assert.DoesNotContain(reportOrg2.Records, r => r.BookingReference == "PB-T1-ATT");
    }

    [Fact]
    public async Task ExportAttendanceReportCsvAsync_GeneratesProperlyFormattedCsv()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);

        var court = new Court { Id = 1, Name = "Arena Court 1", Status = CourtStatus.Active, OrganizationId = Org1 };
        context.Courts.Add(court);

        var booking = new Booking
        {
            Id = 1,
            OrganizationId = Org1,
            BookingReference = "PB-CSV-ATT",
            CustomerName = "Garcia, Ana",
            CustomerPhone = "09179998888",
            CustomerEmail = "ana@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 8),
            StartTime = new TimeSpan(9, 0, 0),
            EndTime = new TimeSpan(10, 0, 0),
            Price = 400m,
            BookingStatus = BookingStatus.Confirmed,
            CheckedInAt = DateTime.UtcNow,
            CheckInMethod = CheckInMethod.QrScan,
            IsNoShow = false
        };

        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        var service = new ReportService(context);

        // Act
        var csvBytes = await service.ExportAttendanceReportCsvAsync(Org1, 2026, 10);
        var csvText = Encoding.UTF8.GetString(csvBytes);

        // Assert
        Assert.NotEmpty(csvBytes);
        Assert.Contains("Attendance, Check-In & No-Show Report", csvText);
        Assert.Contains("PB-CSV-ATT", csvText);
        Assert.Contains("\"Garcia, Ana\"", csvText);
        Assert.Contains("Checked-In", csvText);
        Assert.Contains("QrScan", csvText);
    }

    [Fact]
    public async Task GetCustomerInsightsReportAsync_CalculatesAccurateRetentionAndVIPs()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);

        var court1 = new Court { Id = 1, Name = "VIP Court 1", Status = CourtStatus.Active, OrganizationId = Org1 };
        var court2 = new Court { Id = 2, Name = "Standard Court 2", Status = CourtStatus.Active, OrganizationId = Org1 };
        context.Courts.AddRange(court1, court2);

        // Historical prior booking (September 2026): Customer "Alice" booked before (so she is a Returning Player in October)
        var priorAlice = new Booking
        {
            Id = 1,
            OrganizationId = Org1,
            BookingReference = "PB-PRIOR-01",
            CustomerName = "Alice VIP",
            CustomerPhone = "09170001111",
            CustomerEmail = "alice@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 9, 15),
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(19, 0, 0),
            Price = 500m,
            BookingStatus = BookingStatus.Completed
        };

        // Prior booking for Customer "Lost Player" (August 2026) - No bookings in October (At-Risk Player)
        var priorLost = new Booking
        {
            Id = 2,
            OrganizationId = Org1,
            BookingReference = "PB-PRIOR-02",
            CustomerName = "Inactive Bob",
            CustomerPhone = "09170002222",
            CustomerEmail = "bob@test.com",
            CourtId = 2,
            BookingDate = new DateOnly(2026, 8, 20),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(12, 0, 0),
            Price = 1200m,
            BookingStatus = BookingStatus.Completed
        };

        // October 2026 Bookings for Alice (5 bookings in Oct -> VIP Tier, Total Spend: ₱3,500)
        var octAlice1 = new Booking
        {
            Id = 10,
            OrganizationId = Org1,
            BookingReference = "PB-OCT-A1",
            CustomerName = "Alice VIP",
            CustomerPhone = "09170001111",
            CustomerEmail = "alice@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 2),
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(19, 0, 0),
            Price = 700m,
            BookingStatus = BookingStatus.Confirmed
        };
        var octAlice2 = new Booking
        {
            Id = 11,
            OrganizationId = Org1,
            BookingReference = "PB-OCT-A2",
            CustomerName = "Alice VIP",
            CustomerPhone = "09170001111",
            CustomerEmail = "alice@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 9),
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(19, 0, 0),
            Price = 700m,
            BookingStatus = BookingStatus.Confirmed
        };
        var octAlice3 = new Booking
        {
            Id = 12,
            OrganizationId = Org1,
            BookingReference = "PB-OCT-A3",
            CustomerName = "Alice VIP",
            CustomerPhone = "09170001111",
            CustomerEmail = "alice@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 16),
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(19, 0, 0),
            Price = 700m,
            BookingStatus = BookingStatus.Confirmed
        };
        var octAlice4 = new Booking
        {
            Id = 13,
            OrganizationId = Org1,
            BookingReference = "PB-OCT-A4",
            CustomerName = "Alice VIP",
            CustomerPhone = "09170001111",
            CustomerEmail = "alice@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 23),
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(19, 0, 0),
            Price = 700m,
            BookingStatus = BookingStatus.Confirmed
        };
        var octAlice5 = new Booking
        {
            Id = 14,
            OrganizationId = Org1,
            BookingReference = "PB-OCT-A5",
            CustomerName = "Alice VIP",
            CustomerPhone = "09170001111",
            CustomerEmail = "alice@test.com",
            CourtId = 2,
            BookingDate = new DateOnly(2026, 10, 30),
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(19, 0, 0),
            Price = 700m,
            BookingStatus = BookingStatus.Confirmed
        };

        // October Booking for Charlie (New Customer this month, 1 booking)
        var octCharlie = new Booking
        {
            Id = 20,
            OrganizationId = Org1,
            BookingReference = "PB-OCT-C1",
            CustomerName = "Charlie Newbie",
            CustomerPhone = "09170003333",
            CustomerEmail = "charlie@test.com",
            CourtId = 2,
            BookingDate = new DateOnly(2026, 10, 15),
            StartTime = new TimeSpan(14, 0, 0),
            EndTime = new TimeSpan(15, 0, 0),
            Price = 500m,
            BookingStatus = BookingStatus.Confirmed
        };

        context.Bookings.AddRange(priorAlice, priorLost, octAlice1, octAlice2, octAlice3, octAlice4, octAlice5, octCharlie);
        await context.SaveChangesAsync();

        var service = new ReportService(context);

        // Act
        var report = await service.GetCustomerInsightsReportAsync(Org1, 2026, 10);

        // Assert
        Assert.Equal(Org1, report.OrganizationId);
        Assert.Equal(2, report.TotalUniqueCustomersInMonth); // Alice & Charlie
        Assert.Equal(1, report.ReturningCustomersInMonth); // Alice
        Assert.Equal(1, report.NewCustomersInMonth); // Charlie
        Assert.Equal(50.0m, report.RepeatCustomerRate); // 1 / 2 = 50%
        Assert.Equal(4000m, report.TotalRevenue); // 3500 + 500
        Assert.Equal(2000m, report.AverageSpendPerCustomer); // 4000 / 2

        // Tier distribution
        Assert.Equal(1, report.VipPlayersCount); // Alice (5 bookings)
        Assert.Equal(0, report.RegularPlayersCount);
        Assert.Equal(1, report.OccasionalPlayersCount); // Charlie (1 booking)

        // VIP Player Ranking
        Assert.Equal(2, report.TopPlayers.Count);
        var rank1 = report.TopPlayers[0];
        Assert.Equal("Alice VIP", rank1.CustomerName);
        Assert.Equal(1, rank1.Rank);
        Assert.Equal(5, rank1.TotalBookings);
        Assert.Equal(3500m, rank1.TotalSpend);
        Assert.Equal("VIP Court 1", rank1.PreferredCourt); // 4 out of 5 bookings on Court 1
        Assert.Equal("VIP Tier", rank1.LoyaltyTier);
        Assert.False(rank1.IsNewCustomerThisMonth);

        var rank2 = report.TopPlayers[1];
        Assert.Equal("Charlie Newbie", rank2.CustomerName);
        Assert.True(rank2.IsNewCustomerThisMonth);
        Assert.Equal("Occasional", rank2.LoyaltyTier);

        // At-Risk Customers
        Assert.Single(report.AtRiskCustomers);
        Assert.Equal("Inactive Bob", report.AtRiskCustomers[0].CustomerName);
        Assert.Equal(1200m, report.AtRiskCustomers[0].LifetimeSpend);
        Assert.True(report.AtRiskCustomers[0].DaysSinceLastBooking > 30);

        Assert.NotEmpty(report.StrategicRecommendations);
    }

    [Fact]
    public async Task GetCustomerInsightsReportAsync_EnforcesStrictTenantIsolation()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);
        context.SuppressTenantWriteGuard = true;

        var court1 = new Court { Id = 1, Name = "Org1 Court", Status = CourtStatus.Active, OrganizationId = Org1 };
        var court2 = new Court { Id = 2, Name = "Org2 Court", Status = CourtStatus.Active, OrganizationId = Org2 };
        context.Courts.AddRange(court1, court2);

        // Org 1 Booking (Customer T1)
        var org1Booking = new Booking
        {
            Id = 1,
            OrganizationId = Org1,
            BookingReference = "PB-T1-CUST",
            CustomerName = "Org1 Customer",
            CustomerPhone = "09170000001",
            CustomerEmail = "org1@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 5),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(11, 0, 0),
            Price = 1000m,
            BookingStatus = BookingStatus.Confirmed
        };

        // Org 2 Booking (Customer T2) - Must NEVER leak into Org 1
        var org2Booking = new Booking
        {
            Id = 2,
            OrganizationId = Org2,
            BookingReference = "PB-T2-CUST",
            CustomerName = "Org2 Customer",
            CustomerPhone = "09170000002",
            CustomerEmail = "org2@test.com",
            CourtId = 2,
            BookingDate = new DateOnly(2026, 10, 5),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(11, 0, 0),
            Price = 2500m,
            BookingStatus = BookingStatus.Confirmed
        };

        context.Bookings.AddRange(org1Booking, org2Booking);
        await context.SaveChangesAsync();

        var service = new ReportService(context);

        // Act: Fetch for Org 1
        var reportOrg1 = await service.GetCustomerInsightsReportAsync(Org1, 2026, 10);

        // Assert: Org 1 report contains ONLY Org 1 data
        Assert.Equal(Org1, reportOrg1.OrganizationId);
        Assert.Equal(1, reportOrg1.TotalUniqueCustomersInMonth);
        Assert.Equal(1000m, reportOrg1.TotalRevenue);
        Assert.Single(reportOrg1.TopPlayers);
        Assert.Equal("Org1 Customer", reportOrg1.TopPlayers[0].CustomerName);
        Assert.DoesNotContain(reportOrg1.TopPlayers, p => p.CustomerName == "Org2 Customer");

        // Act & Assert: Fetch for Org 2
        await using var context2 = TestDbContextFactory.CreateInMemory(dbName, Org2);
        var service2 = new ReportService(context2);
        var reportOrg2 = await service2.GetCustomerInsightsReportAsync(Org2, 2026, 10);

        Assert.Equal(Org2, reportOrg2.OrganizationId);
        Assert.Equal(1, reportOrg2.TotalUniqueCustomersInMonth);
        Assert.Equal(2500m, reportOrg2.TotalRevenue);
        Assert.Single(reportOrg2.TopPlayers);
        Assert.Equal("Org2 Customer", reportOrg2.TopPlayers[0].CustomerName);
        Assert.DoesNotContain(reportOrg2.TopPlayers, p => p.CustomerName == "Org1 Customer");
    }

    [Fact]
    public async Task ExportCustomerDirectoryCsvAsync_GeneratesProperlyFormattedCsv()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);

        var court = new Court { Id = 1, Name = "Premier Court 1", Status = CourtStatus.Active, OrganizationId = Org1 };
        context.Courts.Add(court);

        var booking = new Booking
        {
            Id = 1,
            OrganizationId = Org1,
            BookingReference = "PB-CSV-CUST",
            CustomerName = "Ramos, Fernando",
            CustomerPhone = "09175554433",
            CustomerEmail = "fernando@test.com",
            CourtId = 1,
            BookingDate = new DateOnly(2026, 10, 10),
            StartTime = new TimeSpan(16, 0, 0),
            EndTime = new TimeSpan(17, 0, 0),
            Price = 650m,
            BookingStatus = BookingStatus.Confirmed
        };

        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        var service = new ReportService(context);

        // Act
        var csvBytes = await service.ExportCustomerDirectoryCsvAsync(Org1, 2026, 10);
        var csvText = Encoding.UTF8.GetString(csvBytes);

        // Assert
        Assert.NotEmpty(csvBytes);
        Assert.Contains("Customer & Repeat Player Directory", csvText);
        Assert.Contains("\"Ramos, Fernando\"", csvText);
        Assert.Contains("09175554433", csvText);
        Assert.Contains("Premier Court 1", csvText);
        Assert.Contains("Active & Frequent Players Directory", csvText);
    }
}
