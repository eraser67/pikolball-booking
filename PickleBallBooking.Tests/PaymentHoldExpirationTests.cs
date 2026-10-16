using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using Xunit;

namespace PickleBallBooking.Tests;

public class PaymentHoldExpirationTests
{
    private const int TestOrgId = 1;

    private static ApplicationDbContext CreateContext(string dbName)
    {
        return TestDbContextFactory.CreateInMemory(dbName, TestOrgId);
    }

    [Fact]
    public async Task CleanupExpiredPendingBookingsAsync_ReleasesExpiredHoldsAndPreservesRecentHolds()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);

        var court = new Court { Id = 1, Name = "Court 1", Status = CourtStatus.Active, OrganizationId = TestOrgId };
        var slot1 = new TimeSlot { Id = 1, StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(9, 0, 0), Status = TimeSlotStatus.Active };
        var slot2 = new TimeSlot { Id = 2, StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(10, 0, 0), Status = TimeSlotStatus.Active };
        var slot3 = new TimeSlot { Id = 3, StartTime = new TimeSpan(10, 0, 0), EndTime = new TimeSpan(11, 0, 0), Status = TimeSlotStatus.Active };

        context.Courts.Add(court);
        context.TimeSlots.AddRange(slot1, slot2, slot3);

        // Booking 1: 15 minutes old, Pending payment -> SHOULD EXPIRE
        var booking1 = new Booking
        {
            Id = 1,
            OrganizationId = TestOrgId,
            BookingReference = "PB-OLD1",
            CustomerName = "Alice",
            CustomerEmail = "alice@test.com",
            CustomerPhone = "09111111111",
            CourtId = 1,
            BookingDate = AppClock.TodayLocal,
            StartTime = slot1.StartTime,
            EndTime = slot1.EndTime,
            Price = 200m,
            BookingStatus = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddMinutes(-15)
        };
        var bts1 = new BookingTimeSlot
        {
            Id = 1,
            OrganizationId = TestOrgId,
            BookingId = 1,
            CourtId = 1,
            BookingDate = AppClock.TodayLocal,
            TimeSlotId = slot1.Id,
            IsActive = true
        };
        var payment1 = new Payment
        {
            Id = 1,
            OrganizationId = TestOrgId,
            BookingId = 1,
            Amount = 200m,
            PaymentStatus = PaymentStatus.Pending
        };

        // Booking 2: 5 minutes old, Pending payment -> SHOULD STAY ACTIVE
        var booking2 = new Booking
        {
            Id = 2,
            OrganizationId = TestOrgId,
            BookingReference = "PB-RECENT",
            CustomerName = "Bob",
            CustomerEmail = "bob@test.com",
            CustomerPhone = "09222222222",
            CourtId = 1,
            BookingDate = AppClock.TodayLocal,
            StartTime = slot2.StartTime,
            EndTime = slot2.EndTime,
            Price = 200m,
            BookingStatus = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        var bts2 = new BookingTimeSlot
        {
            Id = 2,
            OrganizationId = TestOrgId,
            BookingId = 2,
            CourtId = 1,
            BookingDate = AppClock.TodayLocal,
            TimeSlotId = slot2.Id,
            IsActive = true
        };
        var payment2 = new Payment
        {
            Id = 2,
            OrganizationId = TestOrgId,
            BookingId = 2,
            Amount = 200m,
            PaymentStatus = PaymentStatus.Pending
        };

        // Booking 3: 15 minutes old, but reference was Submitted -> SHOULD STAY ACTIVE
        var booking3 = new Booking
        {
            Id = 3,
            OrganizationId = TestOrgId,
            BookingReference = "PB-SUBMITTED",
            CustomerName = "Charlie",
            CustomerEmail = "charlie@test.com",
            CustomerPhone = "09333333333",
            CourtId = 1,
            BookingDate = AppClock.TodayLocal,
            StartTime = slot3.StartTime,
            EndTime = slot3.EndTime,
            Price = 200m,
            BookingStatus = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddMinutes(-15)
        };
        var bts3 = new BookingTimeSlot
        {
            Id = 3,
            OrganizationId = TestOrgId,
            BookingId = 3,
            CourtId = 1,
            BookingDate = AppClock.TodayLocal,
            TimeSlotId = slot3.Id,
            IsActive = true
        };
        var payment3 = new Payment
        {
            Id = 3,
            OrganizationId = TestOrgId,
            BookingId = 3,
            Amount = 200m,
            PaymentStatus = PaymentStatus.Submitted,
            ReferenceNumber = "REF123456",
            SubmittedAt = DateTime.UtcNow.AddMinutes(-10)
        };

        context.Bookings.AddRange(booking1, booking2, booking3);
        context.BookingTimeSlots.AddRange(bts1, bts2, bts3);
        context.Payments.AddRange(payment1, payment2, payment3);
        await context.SaveChangesAsync();

        var paymentService = new PaymentService(context, new TenantContext { OrganizationId = TestOrgId });

        // Act
        var expiredCount = await paymentService.CleanupExpiredPendingBookingsAsync();

        // Assert
        Assert.Equal(1, expiredCount);

        // Booking 1 should be cancelled and its slot hold released
        var reloadedBooking1 = await context.Bookings.FindAsync(1);
        var reloadedPayment1 = await context.Payments.FindAsync(1);
        var reloadedBts1 = await context.BookingTimeSlots.FindAsync(1);
        Assert.Equal(BookingStatus.Cancelled, reloadedBooking1!.BookingStatus);
        Assert.Equal(PaymentStatus.Cancelled, reloadedPayment1!.PaymentStatus);
        Assert.False(reloadedBts1!.IsActive);

        // Booking 2 should remain Pending and active
        var reloadedBooking2 = await context.Bookings.FindAsync(2);
        var reloadedPayment2 = await context.Payments.FindAsync(2);
        var reloadedBts2 = await context.BookingTimeSlots.FindAsync(2);
        Assert.Equal(BookingStatus.Pending, reloadedBooking2!.BookingStatus);
        Assert.Equal(PaymentStatus.Pending, reloadedPayment2!.PaymentStatus);
        Assert.True(reloadedBts2!.IsActive);

        // Booking 3 should remain Submitted and active
        var reloadedBooking3 = await context.Bookings.FindAsync(3);
        var reloadedPayment3 = await context.Payments.FindAsync(3);
        var reloadedBts3 = await context.BookingTimeSlots.FindAsync(3);
        Assert.Equal(BookingStatus.Pending, reloadedBooking3!.BookingStatus);
        Assert.Equal(PaymentStatus.Submitted, reloadedPayment3!.PaymentStatus);
        Assert.True(reloadedBts3!.IsActive);
    }

    [Fact]
    public async Task SubmitWithResultAsync_ReturnsExpiredWhenBookingIsOlderThan10Minutes()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);

        var booking = new Booking
        {
            Id = 10,
            OrganizationId = TestOrgId,
            BookingReference = "PB-EXPIRED-SUBMIT",
            CustomerName = "David",
            CustomerEmail = "david@test.com",
            CustomerPhone = "09444444444",
            CourtId = 1,
            BookingDate = AppClock.TodayLocal,
            StartTime = new TimeSpan(14, 0, 0),
            EndTime = new TimeSpan(15, 0, 0),
            Price = 250m,
            BookingStatus = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddMinutes(-11)
        };
        var bts = new BookingTimeSlot
        {
            Id = 10,
            OrganizationId = TestOrgId,
            BookingId = 10,
            CourtId = 1,
            BookingDate = AppClock.TodayLocal,
            TimeSlotId = 1,
            IsActive = true
        };
        var payment = new Payment
        {
            Id = 10,
            OrganizationId = TestOrgId,
            BookingId = 10,
            Amount = 250m,
            PaymentStatus = PaymentStatus.Pending
        };

        context.Bookings.Add(booking);
        context.BookingTimeSlots.Add(bts);
        context.Payments.Add(payment);
        await context.SaveChangesAsync();

        var paymentService = new PaymentService(context, new TenantContext { OrganizationId = TestOrgId });

        // Act
        var result = await paymentService.SubmitWithResultAsync(10, "GCASH999999", null);

        // Assert
        Assert.Equal(PaymentSubmitResult.Expired, result);

        var reloadedBooking = await context.Bookings.FindAsync(10);
        var reloadedPayment = await context.Payments.FindAsync(10);
        var reloadedBts = await context.BookingTimeSlots.FindAsync(10);
        Assert.Equal(BookingStatus.Cancelled, reloadedBooking!.BookingStatus);
        Assert.Equal(PaymentStatus.Cancelled, reloadedPayment!.PaymentStatus);
        Assert.False(reloadedBts!.IsActive);
    }
}
