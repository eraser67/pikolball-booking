using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Tests;

public class SemaphoreSmsServiceTests
{
    // ─── Phone Number Normalization Tests ────────────────────────────────────

    [Theory]
    [InlineData("09171234567", "09171234567")]
    [InlineData("+639171234567", "09171234567")]
    [InlineData("639171234567", "09171234567")]
    [InlineData("0917-123-4567", "09171234567")]
    [InlineData("(0917) 123 4567", "09171234567")]
    [InlineData("9171234567", "09171234567")]
    [InlineData("+63 918 999 8888", "09189998888")]
    public void NormalizePhoneNumber_ValidInputs_ReturnsNormalized11Digits(string input, string expected)
    {
        var result = SemaphoreSmsService.NormalizePhoneNumber(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("0281234567")]      // Manila landline (not mobile)
    [InlineData("+14155552671")]    // US phone number
    [InlineData("abcdefghijk")]
    public void NormalizePhoneNumber_InvalidInputs_ReturnsNull(string? input)
    {
        var result = SemaphoreSmsService.NormalizePhoneNumber(input);
        Assert.Null(result);
    }

    // ─── Service Dispatch Tests ──────────────────────────────────────────────

    [Fact]
    public async Task SendSmsAsync_WhenDisabled_ReturnsFalseWithoutCallingHttp()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client  = new HttpClient(handler);
        var options = Options.Create(new SmsOptions { Enabled = false, ApiKey = "fake_key" });
        var service = new SemaphoreSmsService(client, options, NullLogger<SemaphoreSmsService>.Instance);

        var result = await service.SendSmsAsync("09171234567", "Test message");

        Assert.False(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SendSmsAsync_WhenApiKeyMissing_ReturnsFalseWithoutCallingHttp()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client  = new HttpClient(handler);
        var options = Options.Create(new SmsOptions { Enabled = true, ApiKey = "" });
        var service = new SemaphoreSmsService(client, options, NullLogger<SemaphoreSmsService>.Instance);

        var result = await service.SendSmsAsync("09171234567", "Test message");

        Assert.False(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SendSmsAsync_WhenInvalidPhone_ReturnsFalseWithoutCallingHttp()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client  = new HttpClient(handler);
        var options = Options.Create(new SmsOptions { Enabled = true, ApiKey = "valid_key" });
        var service = new SemaphoreSmsService(client, options, NullLogger<SemaphoreSmsService>.Instance);

        var result = await service.SendSmsAsync("invalid-phone", "Test message");

        Assert.False(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SendSmsAsync_WhenSuccessful_PostsNormalizedNumberAndReturnsTrue()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "[{\"message_id\":12345,\"status\":\"Queued\"}]");
        var client  = new HttpClient(handler);
        var options = Options.Create(new SmsOptions
        {
            Enabled = true,
            ApiKey = "test_api_key",
            SenderName = "MYCLUB"
        });
        var service = new SemaphoreSmsService(client, options, NullLogger<SemaphoreSmsService>.Instance);

        var result = await service.SendSmsAsync("+639171234567", "Your booking is confirmed!");

        Assert.True(result);
        Assert.Equal(1, handler.CallCount);
        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("09171234567", handler.LastRequestBody);
        Assert.Contains("test_api_key", handler.LastRequestBody);
        Assert.Contains("MYCLUB", handler.LastRequestBody);
    }

    [Fact]
    public async Task SendSmsAsync_WhenGatewayReturnsError_ReturnsFalseWithoutThrowing()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.BadRequest, "{\"error\":\"Invalid mobile number\"}");
        var client  = new HttpClient(handler);
        var options = Options.Create(new SmsOptions { Enabled = true, ApiKey = "test_key" });
        var service = new SemaphoreSmsService(client, options, NullLogger<SemaphoreSmsService>.Instance);

        var result = await service.SendSmsAsync("09171234567", "Test message");

        Assert.False(result);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SendSmsAsync_WhenHttpThrows_CatchesAndReturnsFalse()
    {
        var handler = new ThrowingHttpMessageHandler();
        var client  = new HttpClient(handler);
        var options = Options.Create(new SmsOptions { Enabled = true, ApiKey = "test_key" });
        var service = new SemaphoreSmsService(client, options, NullLogger<SemaphoreSmsService>.Instance);

        var result = await service.SendSmsAsync("09171234567", "Test message");

        Assert.False(result);
    }

    // ─── BookingSmsService Template Tests ────────────────────────────────────

    [Fact]
    public void BuildBookingReceivedMessage_ContainsExpectedInfo()
    {
        var booking = new Booking
        {
            CustomerName = "Juan Dela Cruz",
            BookingReference = "PB-20260922-0001",
            BookingDate = new DateOnly(2026, 9, 25),
            StartTime = new TimeSpan(14, 0, 0),
            EndTime = new TimeSpan(16, 0, 0),
            Court = new Court { Name = "Center Court" }
        };
        var org = new Organization { Name = "Metro Pickleball Club" };

        var msg = BookingSmsService.BuildBookingReceivedMessage(booking, org);

        Assert.Contains("Juan Dela Cruz", msg);
        Assert.Contains("PB-20260922-0001", msg);
        Assert.Contains("Metro Pickleball Club", msg);
        Assert.Contains("Center Court", msg);
        Assert.Contains("is received", msg);
    }

    [Fact]
    public void BuildPaymentVerifiedMessage_ContainsConfirmationDetails()
    {
        var booking = new Booking
        {
            CustomerName = "Maria Santos",
            BookingReference = "PB-20260922-0002",
            BookingDate = new DateOnly(2026, 9, 26),
            StartTime = new TimeSpan(9, 0, 0),
            EndTime = new TimeSpan(10, 0, 0),
            Court = new Court { Name = "Court 1" }
        };
        var org = new Organization { Name = "Pikolball Academy" };

        var msg = BookingSmsService.BuildPaymentVerifiedMessage(booking, org);

        Assert.Contains("Maria Santos", msg);
        Assert.Contains("PB-20260922-0002", msg);
        Assert.Contains("CONFIRMED", msg);
    }

    [Fact]
    public void BuildPaymentRejectedMessage_ContainsRejectionGuidance()
    {
        var booking = new Booking
        {
            CustomerName = "Pedro Penduko",
            BookingReference = "PB-20260922-0003"
        };
        var org = new Organization { Name = "Pikolball Hub" };

        var msg = BookingSmsService.BuildPaymentRejectedMessage(booking, org);

        Assert.Contains("Pedro Penduko", msg);
        Assert.Contains("PB-20260922-0003", msg);
        Assert.Contains("was not accepted", msg);
    }

    [Fact]
    public void BuildBookingCancelledMessage_ContainsCancellationDetails()
    {
        var booking = new Booking
        {
            CustomerName = "Ana Reyes",
            BookingReference = "PB-20260922-0004",
            BookingDate = new DateOnly(2026, 9, 28)
        };
        var org = new Organization { Name = "Manila Pickle Club" };

        var msg = BookingSmsService.BuildBookingCancelledMessage(booking, org);

        Assert.Contains("Ana Reyes", msg);
        Assert.Contains("PB-20260922-0004", msg);
        Assert.Contains("cancelled", msg);
    }

    // ─── Test Helpers ────────────────────────────────────────────────────────

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseContent;

        public int CallCount { get; private set; }
        public string? LastRequestBody { get; private set; }

        public MockHttpMessageHandler(HttpStatusCode statusCode, string responseContent)
        {
            _statusCode = statusCode;
            _responseContent = responseContent;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseContent)
            };
        }
    }

    private class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Simulated connection timeout");
        }
    }
}
