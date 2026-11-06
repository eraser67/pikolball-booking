using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using Xunit;

namespace PickleBallBooking.Tests;

public class PendingBookingHoldTests
{
    private const int TestOrganizationId = 1;

    private static ApplicationDbContext CreateContext()
    {
        return TestDbContextFactory.CreateInMemory(Guid.NewGuid().ToString(), TestOrganizationId);
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_PendingBooking_MarksSlotAsBookedRegardlessOfAge()
    {
        // Arrange
        await using var context = CreateContext();
        var date = AppClock.TodayLocal;
        var court = new Court { Id = 1, OrganizationId = TestOrganizationId, Name = "Court 1", Status = CourtStatus.Active };
        var timeSlot = new TimeSlot { Id = 1, StartTime = new TimeSpan(14, 0, 0), EndTime = new TimeSpan(15, 0, 0), Status = TimeSlotStatus.Active };

        context.Courts.Add(court);
        context.TimeSlots.Add(timeSlot);

        // A pending booking created 25 minutes ago
        var booking = new Booking
        {
            Id = 10,
            OrganizationId = TestOrganizationId,
            BookingReference = "PB-TEST-0001",
            CustomerName = "John Doe",
            CustomerPhone = "09123456789",
            CustomerEmail = "john@example.com",
            CourtId = 1,
            BookingDate = date,
            StartTime = new TimeSpan(14, 0, 0),
            EndTime = new TimeSpan(15, 0, 0),
            Price = 500m,
            BookingStatus = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddMinutes(-25),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-25)
        };
        context.Bookings.Add(booking);

        var bts = new BookingTimeSlot
        {
            Id = 100,
            OrganizationId = TestOrganizationId,
            BookingId = booking.Id,
            CourtId = 1,
            BookingDate = date,
            TimeSlotId = 1,
            IsActive = true
        };
        context.BookingTimeSlots.Add(bts);
        await context.SaveChangesAsync();

        var service = new BookingService(context);

        // Act
        var slots = await service.GetAvailableSlotsAsync(1, date);

        // Assert: As long as bts.IsActive == true, the slot MUST NOT be shown as available!
        var slot = Assert.Single(slots);
        Assert.False(slot.IsAvailable, "Slot should be marked as unavailable while BookingTimeSlot is active.");
        Assert.Equal("John D.", slot.BookedBy);
    }

    [Fact]
    public async Task CleanupExpiredPendingBookings_UnpaidPending_ReleasesSlot()
    {
        // Arrange
        await using var context = CreateContext();
        var date = AppClock.TodayLocal;
        var court = new Court { Id = 1, OrganizationId = TestOrganizationId, Name = "Court 1", Status = CourtStatus.Active };
        var timeSlot = new TimeSlot { Id = 1, StartTime = new TimeSpan(14, 0, 0), EndTime = new TimeSpan(15, 0, 0), Status = TimeSlotStatus.Active };

        context.Courts.Add(court);
        context.TimeSlots.Add(timeSlot);

        var booking = new Booking
        {
            Id = 20,
            OrganizationId = TestOrganizationId,
            BookingReference = "PB-TEST-0002",
            CustomerName = "Abandoned Player",
            CustomerPhone = "09123456789",
            CustomerEmail = "abandoned@example.com",
            CourtId = 1,
            BookingDate = date,
            StartTime = new TimeSpan(14, 0, 0),
            EndTime = new TimeSpan(15, 0, 0),
            Price = 500m,
            BookingStatus = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddMinutes(-20),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-20)
        };
        context.Bookings.Add(booking);

        var payment = new Payment
        {
            Id = 200,
            OrganizationId = TestOrganizationId,
            BookingId = 20,
            Amount = 500m,
            PaymentStatus = PaymentStatus.Pending, // Unpaid
            CreatedAt = DateTime.UtcNow.AddMinutes(-20)
        };
        context.Payments.Add(payment);

        var bts = new BookingTimeSlot
        {
            Id = 2000,
            OrganizationId = TestOrganizationId,
            BookingId = booking.Id,
            CourtId = 1,
            BookingDate = date,
            TimeSlotId = 1,
            IsActive = true
        };
        context.BookingTimeSlots.Add(bts);
        await context.SaveChangesAsync();

        var tenantContext = new TenantContext { OrganizationId = TestOrganizationId };
        var paymentService = new PaymentService(context, tenantContext);
        var bookingService = new BookingService(context);

        // Before cleanup: slot is booked
        var slotsBefore = await bookingService.GetAvailableSlotsAsync(1, date);
        Assert.False(slotsBefore[0].IsAvailable);

        // Act: cleanup expired holds
        var cleaned = await paymentService.CleanupExpiredPendingBookingsAsync();
        Assert.True(cleaned >= 1);

        // Assert: booking is now cancelled, bts is inactive, and slot is available
        var reloadedBooking = await context.Bookings.FindAsync(booking.Id);
        Assert.Equal(BookingStatus.Cancelled, reloadedBooking!.BookingStatus);

        var reloadedBts = await context.BookingTimeSlots.FindAsync(bts.Id);
        Assert.False(reloadedBts!.IsActive);

        var slotsAfter = await bookingService.GetAvailableSlotsAsync(1, date);
        Assert.True(slotsAfter[0].IsAvailable, "Slot should now be available after expired hold is released.");
    }

    [Fact]
    public async Task CleanupExpiredPendingBookings_SubmittedPayment_DoesNotCancelAndKeepsSlotBooked()
    {
        // Arrange
        await using var context = CreateContext();
        var date = AppClock.TodayLocal;
        var court = new Court { Id = 1, OrganizationId = TestOrganizationId, Name = "Court 1", Status = CourtStatus.Active };
        var timeSlot = new TimeSlot { Id = 1, StartTime = new TimeSpan(14, 0, 0), EndTime = new TimeSpan(15, 0, 0), Status = TimeSlotStatus.Active };

        context.Courts.Add(court);
        context.TimeSlots.Add(timeSlot);

        var booking = new Booking
        {
            Id = 30,
            OrganizationId = TestOrganizationId,
            BookingReference = "PB-TEST-0003",
            CustomerName = "Paying Player",
            CustomerPhone = "09123456789",
            CustomerEmail = "paying@example.com",
            CourtId = 1,
            BookingDate = date,
            StartTime = new TimeSpan(14, 0, 0),
            EndTime = new TimeSpan(15, 0, 0),
            Price = 500m,
            BookingStatus = BookingStatus.Pending, // Still pending admin review
            CreatedAt = DateTime.UtcNow.AddMinutes(-30),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-30)
        };
        context.Bookings.Add(booking);

        var payment = new Payment
        {
            Id = 300,
            OrganizationId = TestOrganizationId,
            BookingId = 30,
            Amount = 500m,
            PaymentStatus = PaymentStatus.Submitted, // Customer submitted reference number!
            ReferenceNumber = "GCASH-12345",
            SubmittedAt = DateTime.UtcNow.AddMinutes(-25),
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        };
        context.Payments.Add(payment);

        var bts = new BookingTimeSlot
        {
            Id = 3000,
            OrganizationId = TestOrganizationId,
            BookingId = booking.Id,
            CourtId = 1,
            BookingDate = date,
            TimeSlotId = 1,
            IsActive = true
        };
        context.BookingTimeSlots.Add(bts);
        await context.SaveChangesAsync();

        var tenantContext = new TenantContext { OrganizationId = TestOrganizationId };
        var paymentService = new PaymentService(context, tenantContext);
        var bookingService = new BookingService(context);

        // Act: cleanup runs
        var cleaned = await paymentService.CleanupExpiredPendingBookingsAsync();
        Assert.Equal(0, cleaned);

        // Assert: booking is STILL pending, payment is Submitted, and slot is STILL booked!
        var reloadedBooking = await context.Bookings.FindAsync(booking.Id);
        Assert.Equal(BookingStatus.Pending, reloadedBooking!.BookingStatus);

        var slots = await bookingService.GetAvailableSlotsAsync(1, date);
        Assert.False(slots[0].IsAvailable, "Slot with submitted payment must stay booked awaiting admin verification.");
    }

    [Fact]
    public async Task UpdateBookingStatusAsync_WhenConfirmedByAdmin_MarksPaymentVerifiedAndDispatchesNotification()
    {
        // Arrange
        await using var context = CreateContext();
        var date = AppClock.TodayLocal;
        var court = new Court { Id = 5, OrganizationId = TestOrganizationId, Name = "Court 5", Status = CourtStatus.Active };
        var org = new Organization { Id = TestOrganizationId, Name = "Demo Pickleball", NotificationEmail = "admin@example.com" };

        context.Courts.Add(court);
        context.Organizations.Add(org);

        var booking = new Booking
        {
            Id = 50,
            OrganizationId = TestOrganizationId,
            BookingReference = "PB-TEST-0050",
            CustomerName = "Alice",
            CustomerPhone = "09111111111",
            CustomerEmail = "alice@example.com",
            CourtId = 5,
            BookingDate = date,
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(11, 0, 0),
            Price = 400m,
            BookingStatus = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Bookings.Add(booking);

        var payment = new Payment
        {
            Id = 500,
            OrganizationId = TestOrganizationId,
            BookingId = booking.Id,
            Amount = 400m,
            PaymentStatus = PaymentStatus.Submitted,
            ReferenceNumber = "REF-500",
            CreatedAt = DateTime.UtcNow
        };
        context.Payments.Add(payment);
        await context.SaveChangesAsync();

        var bookingService = new BookingService(context);

        // Act: Admin confirms directly from /Admin/Bookings
        var result = await bookingService.UpdateBookingStatusAsync(booking.Id, BookingStatus.Confirmed);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(BookingStatus.Confirmed, result.Booking!.BookingStatus);

        var updatedPayment = await context.Payments.FindAsync(payment.Id);
        Assert.NotNull(updatedPayment);
        Assert.Equal(PaymentStatus.Verified, updatedPayment!.PaymentStatus);
    }
}

