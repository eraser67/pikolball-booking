using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using Xunit;

namespace PickleBallBooking.Tests;

/// <summary>
/// Phase 25: tests for payment status transitions and booking/payment relationship integrity.
///
/// These tests use in-memory EF Core and verify:
/// - Payment record is created for a booking
/// - Submission transitions Pending → Submitted
/// - Verification transitions Submitted → Verified (without corrupting booking)
/// - Rejection transitions Submitted → Rejected (without corrupting booking)
/// - Duplicate GCash reference is rejected
/// - Booking architecture (TimeSlot/CourtTimeSlot/BookingTimeSlot) is preserved
/// - Payment cancellation on booking cancellation
/// </summary>
public class PaymentStatusTransitionTests
{
    private const string DbPrefix = nameof(PaymentStatusTransitionTests);

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static (ApplicationDbContext Ctx, IPaymentService Svc) CreateForOrg(string dbName, int orgId)
    {
        var ctx = TestDbContextFactory.CreateInMemory(dbName, orgId);
        var svc = new PaymentService(ctx, TestDbContextFactory.Tenant(orgId));
        return (ctx, svc);
    }

    private static Models.Booking SeedBooking(ApplicationDbContext ctx, int orgId)
    {
        var booking = new Models.Booking
        {
            OrganizationId    = orgId,
            BookingReference  = $"REF-{Guid.NewGuid():N}",
            CustomerName      = "Test Customer",
            CustomerEmail     = "test@example.com",
            CustomerPhone     = "09171234567",
            BookingDate       = DateOnly.FromDateTime(DateTime.Today),
            StartTime         = TimeSpan.FromHours(9),
            EndTime           = TimeSpan.FromHours(10),
            Price             = 500m,
            BookingStatus     = BookingStatus.Pending,
            CreatedAt         = DateTime.UtcNow,
            UpdatedAt         = DateTime.UtcNow
        };
        ctx.Bookings.Add(booking);
        ctx.SaveChanges();
        return booking;
    }

    // ─── Scenario A: Payment record created for booking ──────────────────────

    [Fact]
    public async Task CreateForBookingAsync_NewBooking_CreatesPaymentRecord()
    {
        var (ctx, svc) = CreateForOrg($"{DbPrefix}_CreateNew_1", orgId: 1);
        var booking    = SeedBooking(ctx, 1);

        var payment = await svc.CreateForBookingAsync(booking.Id);

        Assert.NotNull(payment);
        Assert.Equal(booking.Id, payment!.BookingId);
        Assert.Equal(PaymentStatus.Pending, payment.PaymentStatus);
        Assert.Equal(PaymentMethod.GCash, payment.PaymentMethod);
        Assert.Equal(booking.Price, payment.Amount);
    }

    [Fact]
    public async Task CreateForBookingAsync_CalledTwice_IsIdempotent()
    {
        var (ctx, svc) = CreateForOrg($"{DbPrefix}_Idempotent_1", orgId: 1);
        var booking    = SeedBooking(ctx, 1);

        var p1 = await svc.CreateForBookingAsync(booking.Id);
        var p2 = await svc.CreateForBookingAsync(booking.Id);

        Assert.NotNull(p1);
        Assert.NotNull(p2);
        Assert.Equal(p1!.Id, p2!.Id); // same payment returned, no duplicate

        var count = ctx.Payments.Count();
        Assert.Equal(1, count);
    }

    // ─── Scenario B: Customer submits reference ──────────────────────────────

    [Fact]
    public async Task SubmitAsync_PendingPayment_TransitionsToSubmitted()
    {
        var (ctx, svc) = CreateForOrg($"{DbPrefix}_Submit_1", orgId: 1);
        var booking    = SeedBooking(ctx, 1);
        var payment    = await svc.CreateForBookingAsync(booking.Id);
        Assert.NotNull(payment);

        var ok = await svc.SubmitAsync(payment!.Id, "GCash123456", null);

        Assert.True(ok);

        var updated = ctx.Payments.AsNoTracking().First(p => p.Id == payment.Id);
        Assert.Equal(PaymentStatus.Submitted, updated.PaymentStatus);
        Assert.Equal("GCash123456", updated.ReferenceNumber);
        Assert.NotNull(updated.SubmittedAt);
    }

    [Fact]
    public async Task SubmitAsync_EmptyReference_ReturnsFalse()
    {
        var (ctx, svc) = CreateForOrg($"{DbPrefix}_EmptyRef_1", orgId: 1);
        var booking    = SeedBooking(ctx, 1);
        var payment    = await svc.CreateForBookingAsync(booking.Id);
        Assert.NotNull(payment);

        var ok = await svc.SubmitAsync(payment!.Id, "   ", null);

        Assert.False(ok);
        var unchanged = ctx.Payments.AsNoTracking().First(p => p.Id == payment.Id);
        Assert.Equal(PaymentStatus.Pending, unchanged.PaymentStatus);
    }

    [Fact]
    public async Task SubmitAsync_SubmittedPayment_CannotBeResubmitted()
    {
        var (ctx, svc) = CreateForOrg($"{DbPrefix}_Resubmit_1", orgId: 1);
        var booking    = SeedBooking(ctx, 1);
        var payment    = await svc.CreateForBookingAsync(booking.Id);
        Assert.NotNull(payment);

        await svc.SubmitAsync(payment!.Id, "GCash111", null);
        var ok = await svc.SubmitAsync(payment.Id, "GCash222", null); // second attempt

        Assert.False(ok);
    }

    // ─── Test 8: Duplicate GCash reference validation ────────────────────────

    [Fact]
    public async Task SubmitAsync_DuplicateGCashReference_IsRejected()
    {
        // Uses a single shared database so both payments can see each other
        // (mirrors the cross-tenant IgnoreQueryFilters check).
        const string dbName = $"{DbPrefix}_DupRef_1";
        var ctx1 = TestDbContextFactory.CreateInMemory(dbName, 1);
        var ctx2 = TestDbContextFactory.CreateInMemory(dbName, 1);

        var svc1 = new PaymentService(ctx1, TestDbContextFactory.Tenant(1));

        var booking1 = SeedBooking(ctx1, 1);
        var booking2 = SeedBooking(ctx1, 1);

        var p1 = await svc1.CreateForBookingAsync(booking1.Id);
        var p2 = await svc1.CreateForBookingAsync(booking2.Id);
        Assert.NotNull(p1);
        Assert.NotNull(p2);

        // First submission succeeds.
        var ok1 = await svc1.SubmitAsync(p1!.Id, "GCash-DUPE-999", null);
        Assert.True(ok1);

        // Second booking tries to use the same reference: must be rejected.
        var ok2 = await svc1.SubmitAsync(p2!.Id, "GCash-DUPE-999", null);
        Assert.False(ok2);

        var p2Updated = ctx2.Payments.AsNoTracking().First(p => p.Id == p2.Id);
        Assert.Equal(PaymentStatus.Pending, p2Updated.PaymentStatus);
    }

    [Fact]
    public async Task SubmitAsync_UniqueGCashReference_IsAccepted()
    {
        const string dbName = $"{DbPrefix}_UniqueRef_1";
        var ctx = TestDbContextFactory.CreateInMemory(dbName, 1);
        var svc = new PaymentService(ctx, TestDbContextFactory.Tenant(1));

        var booking1 = SeedBooking(ctx, 1);
        var booking2 = SeedBooking(ctx, 1);
        var p1 = await svc.CreateForBookingAsync(booking1.Id);
        var p2 = await svc.CreateForBookingAsync(booking2.Id);
        Assert.NotNull(p1);
        Assert.NotNull(p2);

        var ok1 = await svc.SubmitAsync(p1!.Id, "GCash-AAA", null);
        var ok2 = await svc.SubmitAsync(p2!.Id, "GCash-BBB", null); // different reference

        Assert.True(ok1);
        Assert.True(ok2);
    }

    // ─── Scenario C: Admin verifies payment ──────────────────────────────────

    [Fact]
    public async Task VerifyAsync_SubmittedPayment_TransitionsToVerified()
    {
        var (ctx, svc) = CreateForOrg($"{DbPrefix}_Verify_1", orgId: 1);
        var booking    = SeedBooking(ctx, 1);
        var payment    = await svc.CreateForBookingAsync(booking.Id);
        Assert.NotNull(payment);
        await svc.SubmitAsync(payment!.Id, "GCash-VERIFY", null);

        var ok = await svc.VerifyAsync(payment.Id, "admin-user-id");

        Assert.True(ok);
        var updated = ctx.Payments.AsNoTracking().First(p => p.Id == payment.Id);
        Assert.Equal(PaymentStatus.Verified, updated.PaymentStatus);
        Assert.Equal("admin-user-id", updated.VerifiedByUserId);
        Assert.NotNull(updated.VerifiedAt);
    }

    [Fact]
    public async Task VerifyAsync_DoesNotModifyBooking()
    {
        var (ctx, svc) = CreateForOrg($"{DbPrefix}_Verify_BookingIntact_1", orgId: 1);
        var booking    = SeedBooking(ctx, 1);
        var payment    = await svc.CreateForBookingAsync(booking.Id);
        Assert.NotNull(payment);
        await svc.SubmitAsync(payment!.Id, "GCash-VBI", null);

        var bookingBefore = ctx.Bookings.AsNoTracking().First(b => b.Id == booking.Id);
        await svc.VerifyAsync(payment.Id, "admin");
        var bookingAfter = ctx.Bookings.AsNoTracking().First(b => b.Id == booking.Id);

        // Booking transitions from Pending to Confirmed on payment verification; other details remain intact.
        Assert.Equal(BookingStatus.Confirmed,        bookingAfter.BookingStatus);
        Assert.Equal(bookingBefore.BookingReference, bookingAfter.BookingReference);
        Assert.Equal(bookingBefore.CustomerName,     bookingAfter.CustomerName);
        Assert.Equal(bookingBefore.Price,            bookingAfter.Price);
    }

    [Fact]
    public async Task VerifyAsync_PendingPayment_CannotVerify()
    {
        var (ctx, svc) = CreateForOrg($"{DbPrefix}_VerifyPending_1", orgId: 1);
        var booking    = SeedBooking(ctx, 1);
        var payment    = await svc.CreateForBookingAsync(booking.Id);
        Assert.NotNull(payment);

        // Not submitted yet — verify must fail.
        var ok = await svc.VerifyAsync(payment!.Id, "admin");

        Assert.False(ok);
        var unchanged = ctx.Payments.AsNoTracking().First(p => p.Id == payment.Id);
        Assert.Equal(PaymentStatus.Pending, unchanged.PaymentStatus);
    }

    // ─── Scenario D: Admin rejects payment ───────────────────────────────────

    [Fact]
    public async Task RejectAsync_SubmittedPayment_TransitionsToRejected()
    {
        var (ctx, svc) = CreateForOrg($"{DbPrefix}_Reject_1", orgId: 1);
        var booking    = SeedBooking(ctx, 1);
        var payment    = await svc.CreateForBookingAsync(booking.Id);
        Assert.NotNull(payment);
        await svc.SubmitAsync(payment!.Id, "GCash-REJECT", null);

        var ok = await svc.RejectAsync(payment.Id, "admin", "Wrong amount");

        Assert.True(ok);
        var updated = ctx.Payments.AsNoTracking().First(p => p.Id == payment.Id);
        Assert.Equal(PaymentStatus.Rejected, updated.PaymentStatus);
        Assert.Equal("Wrong amount", updated.Notes);
    }

    [Fact]
    public async Task RejectAsync_DoesNotModifyBooking()
    {
        var (ctx, svc) = CreateForOrg($"{DbPrefix}_Reject_BookingIntact_1", orgId: 1);
        var booking    = SeedBooking(ctx, 1);
        var payment    = await svc.CreateForBookingAsync(booking.Id);
        Assert.NotNull(payment);
        await svc.SubmitAsync(payment!.Id, "GCash-RBI", null);

        var bookingBefore = ctx.Bookings.AsNoTracking().First(b => b.Id == booking.Id);
        await svc.RejectAsync(payment.Id, "admin", null);
        var bookingAfter = ctx.Bookings.AsNoTracking().First(b => b.Id == booking.Id);

        Assert.Equal(bookingBefore.BookingStatus,    bookingAfter.BookingStatus);
        Assert.Equal(bookingBefore.BookingReference, bookingAfter.BookingReference);
    }

    // ─── Payment cancellation ────────────────────────────────────────────────

    [Fact]
    public async Task CancelForBookingAsync_PendingPayment_TransitionsToCancelled()
    {
        var (ctx, svc) = CreateForOrg($"{DbPrefix}_Cancel_1", orgId: 1);
        var booking    = SeedBooking(ctx, 1);
        var payment    = await svc.CreateForBookingAsync(booking.Id);
        Assert.NotNull(payment);

        await svc.CancelForBookingAsync(booking.Id);

        var cancelled = ctx.Payments.AsNoTracking().First(p => p.Id == payment!.Id);
        Assert.Equal(PaymentStatus.Cancelled, cancelled.PaymentStatus);
    }

    // ─── Payment settings ────────────────────────────────────────────────────

    [Fact]
    public async Task SavePaymentSettingsAsync_MissingAccountName_ReturnsFalse()
    {
        var (_, svc) = CreateForOrg($"{DbPrefix}_Settings_1", orgId: 1);

        var ok = await svc.SavePaymentSettingsAsync("", "09171234567", null, null, true);

        Assert.False(ok);
    }

    [Fact]
    public async Task SavePaymentSettingsAsync_ValidInput_PersistsSettings()
    {
        var (ctx, svc) = CreateForOrg($"{DbPrefix}_Settings_2", orgId: 1);

        var ok = await svc.SavePaymentSettingsAsync("Juan Dela Cruz", "09171234567", "Send payment", null, true);

        Assert.True(ok);

        var loaded = await svc.GetPaymentSettingsAsync();
        Assert.NotNull(loaded);
        Assert.Equal("Juan Dela Cruz", loaded!.AccountName);
        Assert.Equal("09171234567",    loaded.AccountNumber);
        Assert.True(loaded.IsActive);
    }

    [Fact]
    public async Task SavePaymentSettingsAsync_MissingAccountNumber_WithAccountName_ReturnsTrueAndPersists()
    {
        var (ctx, svc) = CreateForOrg($"{DbPrefix}_Settings_3", orgId: 1);

        // Account number is optional (e.g. when venue uses a QR code).
        var ok = await svc.SavePaymentSettingsAsync("Juan Dela Cruz", null, "Scan QR to pay", "organizations/1/qr/gcash.jpg", true);

        Assert.True(ok);

        var loaded = await svc.GetPaymentSettingsAsync();
        Assert.NotNull(loaded);
        Assert.Equal("Juan Dela Cruz", loaded!.AccountName);
        Assert.Equal(string.Empty,    loaded.AccountNumber);
        Assert.Equal("organizations/1/qr/gcash.jpg", loaded.QRCodeImagePath);
        Assert.True(loaded.IsActive);
    }
}
