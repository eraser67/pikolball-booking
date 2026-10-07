using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Pages.Booking;
using PickleBallBooking.Services;
using PickleBallBooking.Tests.Infrastructure;
using Xunit;

namespace PickleBallBooking.Tests.Pages;

public class ConfirmationModelTests
{
    private const int TestOrgId = 1;

    private static ApplicationDbContext CreateContext()
    {
        return TestDbContextFactory.CreateInMemory(Guid.NewGuid().ToString(), TestOrgId);
    }

    [Fact]
    public async Task ConfirmationModel_OnGetAsync_RespectsEnableAiPaymentVerification_True()
    {
        await using var context = CreateContext();

        var org = new Organization
        {
            Id = TestOrgId,
            Name = "Test Venue",
            Slug = "testvenue",
            Status = OrganizationStatus.Active,
            EnableAiPaymentVerification = true
        };
        context.Organizations.Add(org);

        var court1 = new Court { Id = 1, OrganizationId = TestOrgId, Name = "Court 1", Status = CourtStatus.Active };
        context.Courts.Add(court1);

        var booking = new Booking
        {
            Id = 10,
            OrganizationId = TestOrgId,
            CourtId = 1,
            BookingReference = "PB-TEST-0001",
            BookingDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            StartTime = new TimeSpan(8, 0, 0),
            EndTime = new TimeSpan(9, 0, 0),
            CustomerName = "John Doe",
            CustomerEmail = "john@example.com",
            CustomerPhone = "09170000000",
            Price = 300m,
            BookingStatus = BookingStatus.Pending
        };
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        var tenantContext = new TenantContext { OrganizationId = TestOrgId };
        var paymentService = new PaymentService(context, tenantContext);
        var mockStorage = new MockPaymentProofStorage();

        var model = new ConfirmationModel(context, paymentService, mockStorage);
        await model.OnGetAsync("PB-TEST-0001");

        Assert.NotNull(model.Booking);
        Assert.Equal("PB-TEST-0001", model.Booking.BookingReference);
        Assert.True(model.EnableAiPaymentVerification);
    }

    [Fact]
    public async Task ConfirmationModel_OnGetAsync_RespectsEnableAiPaymentVerification_False()
    {
        await using var context = CreateContext();

        var org = new Organization
        {
            Id = TestOrgId,
            Name = "Test Venue",
            Slug = "testvenue",
            Status = OrganizationStatus.Active,
            EnableAiPaymentVerification = false
        };
        context.Organizations.Add(org);

        var court2 = new Court { Id = 2, OrganizationId = TestOrgId, Name = "Court 2", Status = CourtStatus.Active };
        context.Courts.Add(court2);

        var booking = new Booking
        {
            Id = 11,
            OrganizationId = TestOrgId,
            CourtId = 2,
            BookingReference = "PB-TEST-0002",
            BookingDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            StartTime = new TimeSpan(8, 0, 0),
            EndTime = new TimeSpan(9, 0, 0),
            CustomerName = "Jane Smith",
            CustomerEmail = "jane@example.com",
            CustomerPhone = "09170000001",
            Price = 300m,
            BookingStatus = BookingStatus.Pending
        };
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        var tenantContext = new TenantContext { OrganizationId = TestOrgId };
        var paymentService = new PaymentService(context, tenantContext);
        var mockStorage = new MockPaymentProofStorage();

        var model = new ConfirmationModel(context, paymentService, mockStorage);
        await model.OnGetAsync("PB-TEST-0002");

        Assert.NotNull(model.Booking);
        Assert.Equal("PB-TEST-0002", model.Booking.BookingReference);
        Assert.False(model.EnableAiPaymentVerification);
    }

    [Fact]
    public async Task PaymentConfirmationModel_OnGetAsync_LoadsPaymentInfoSuccessfully()
    {
        await using var context = CreateContext();

        var org = new Organization
        {
            Id = TestOrgId,
            Name = "Test Venue",
            Slug = "testvenue",
            Status = OrganizationStatus.Active
        };
        context.Organizations.Add(org);

        var court = new Court
        {
            Id = 1,
            OrganizationId = TestOrgId,
            Name = "Center Court",
            Status = CourtStatus.Active
        };
        context.Courts.Add(court);

        var booking = new Booking
        {
            Id = 12,
            OrganizationId = TestOrgId,
            CourtId = 1,
            BookingReference = "PB-TEST-0003",
            BookingDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(11, 0, 0),
            CustomerName = "Alice",
            CustomerEmail = "alice@example.com",
            CustomerPhone = "09170000002",
            Price = 250m,
            BookingStatus = BookingStatus.Pending
        };
        context.Bookings.Add(booking);

        var payment = new Payment
        {
            Id = 5,
            OrganizationId = TestOrgId,
            BookingId = 12,
            Amount = 250m,
            PaymentMethod = PaymentMethod.GCash,
            PaymentStatus = PaymentStatus.Submitted,
            ReferenceNumber = "8045858980078",
            SubmittedAt = DateTime.UtcNow
        };
        context.Payments.Add(payment);
        await context.SaveChangesAsync();

        var tenantContext = new TenantContext { OrganizationId = TestOrgId };
        var paymentService = new PaymentService(context, tenantContext);
        var model = new PaymentConfirmationModel(paymentService);

        await model.OnGetAsync("PB-TEST-0003", false);

        Assert.NotNull(model.PaymentInfo);
        Assert.Equal("PB-TEST-0003", model.PaymentInfo.BookingReference);
        Assert.Equal(PaymentStatus.Submitted, model.PaymentInfo.Status);
        Assert.Equal("8045858980078", model.PaymentInfo.ReferenceNumber);
    }

    private sealed class MockPaymentProofStorage : IPaymentProofStorage
    {
        public Task<string> UploadProofAsync(int organizationId, int paymentId, Microsoft.AspNetCore.Http.IFormFile file, System.Threading.CancellationToken ct = default)
            => Task.FromResult("proofs/test.jpg");

        public Task<string> UploadQRCodeAsync(int organizationId, Microsoft.AspNetCore.Http.IFormFile file, System.Threading.CancellationToken ct = default)
            => Task.FromResult("qr/test.jpg");

        public Task DeleteProofAsync(string? storagePath, System.Threading.CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<string?> GetProofSignedUrlAsync(string? storagePath, int expiresInSeconds = 900, System.Threading.CancellationToken ct = default)
            => Task.FromResult<string?>("https://example.com/test.jpg");

        public string? GetQRCodePublicUrl(string? storagePath) => storagePath != null ? $"https://example.com/{storagePath}" : null;
    }
}
