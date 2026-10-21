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
}
