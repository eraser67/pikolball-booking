using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Tests;

public class TelegramServiceTests
{
    // ─── Dispatch & Safety Tests ─────────────────────────────────────────────

    [Fact]
    public async Task SendMessageAsync_WhenDisabled_ReturnsFalseWithoutCallingHttp()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{\"ok\":true}");
        var client  = new HttpClient(handler);
        var options = Options.Create(new TelegramOptions { Enabled = false, BotToken = "123:ABC" });
        var service = new TelegramService(client, options, NullLogger<TelegramService>.Instance);

        var result = await service.SendMessageAsync("123456", "Test message");

        Assert.False(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SendMessageAsync_WhenBotTokenMissing_ReturnsFalseWithoutCallingHttp()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{\"ok\":true}");
        var client  = new HttpClient(handler);
        var options = Options.Create(new TelegramOptions { Enabled = true, BotToken = "" });
        var service = new TelegramService(client, options, NullLogger<TelegramService>.Instance);

        var result = await service.SendMessageAsync("123456", "Test message");

        Assert.False(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SendMessageAsync_WhenChatIdMissing_ReturnsFalseWithoutCallingHttp()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{\"ok\":true}");
        var client  = new HttpClient(handler);
        var options = Options.Create(new TelegramOptions { Enabled = true, BotToken = "123:ABC" });
        var service = new TelegramService(client, options, NullLogger<TelegramService>.Instance);

        var result = await service.SendMessageAsync("", "Test message");

        Assert.False(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SendMessageAsync_WhenSuccessful_PostsJsonAndReturnsTrue()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{\"ok\":true,\"result\":{\"message_id\":10}}");
        var client  = new HttpClient(handler);
        var options = Options.Create(new TelegramOptions
        {
            Enabled = true,
            BotToken = "test_bot_token"
        });
        var service = new TelegramService(client, options, NullLogger<TelegramService>.Instance);

        var result = await service.SendMessageAsync("-1001234567890", "📋 *New Booking Received*");

        Assert.True(result);
        Assert.Equal(1, handler.CallCount);
        Assert.NotNull(handler.LastRequestUri);
        Assert.Contains("bottest_bot_token/sendMessage", handler.LastRequestUri.ToString());
        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("-1001234567890", handler.LastRequestBody);
        Assert.Contains("New Booking Received", handler.LastRequestBody);
    }

    [Fact]
    public async Task SendMessageAsync_WhenApiReturnsError_ReturnsFalseWithoutThrowing()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.BadRequest, "{\"ok\":false,\"description\":\"Bad Request: chat not found\"}");
        var client  = new HttpClient(handler);
        var options = Options.Create(new TelegramOptions { Enabled = true, BotToken = "123:ABC" });
        var service = new TelegramService(client, options, NullLogger<TelegramService>.Instance);

        var result = await service.SendMessageAsync("invalid_chat", "Test message");

        Assert.False(result);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SendMessageAsync_WhenHttpThrows_CatchesAndReturnsFalse()
    {
        var handler = new ThrowingHttpMessageHandler();
        var client  = new HttpClient(handler);
        var options = Options.Create(new TelegramOptions { Enabled = true, BotToken = "123:ABC" });
        var service = new TelegramService(client, options, NullLogger<TelegramService>.Instance);

        var result = await service.SendMessageAsync("123456", "Test message");

        Assert.False(result);
    }

    // ─── BookingTelegramService Template & Resolution Tests ──────────────────

    [Fact]
    public void BuildNewBookingAlertMessage_ContainsAllBookingDetails()
    {
        var booking = new Booking
        {
            CustomerName = "Carlos Yulo",
            CustomerPhone = "09171234567",
            BookingReference = "PB-20260922-0099",
            BookingDate = new DateOnly(2026, 9, 25),
            StartTime = new TimeSpan(15, 0, 0),
            EndTime = new TimeSpan(17, 0, 0),
            Price = 1200.00m,
            Court = new Court { Name = "Championship Court" }
        };
        var org = new Organization { Name = "Manila Pickle Club" };

        var msg = BookingTelegramService.BuildNewBookingAlertMessage(booking, org);

        Assert.Contains("Carlos Yulo", msg);
        Assert.Contains("09171234567", msg);
        Assert.Contains("PB-20260922-0099", msg);
        Assert.Contains("Manila Pickle Club", msg);
        Assert.Contains("Championship Court", msg);
        Assert.Contains("New Booking Received", msg);
    }

    [Fact]
    public void BuildPaymentSubmittedAlertMessage_ContainsPaymentProofDetails()
    {
        var booking = new Booking
        {
            CustomerName = "Hidilyn Diaz",
            BookingReference = "PB-20260922-0100"
        };
        var payment = new Payment
        {
            ReferenceNumber = "GCASH-998877",
            Amount = 600.00m
        };
        var org = new Organization { Name = "Olympic Pickleball Academy" };

        var msg = BookingTelegramService.BuildPaymentSubmittedAlertMessage(booking, payment, org);

        Assert.Contains("Hidilyn Diaz", msg);
        Assert.Contains("PB-20260922-0100", msg);
        Assert.Contains("GCASH-998877", msg);
        Assert.Contains("Payment Proof Submitted", msg);
    }

    [Fact]
    public void BuildBookingCancelledAlertMessage_ContainsCancellationDetails()
    {
        var booking = new Booking
        {
            CustomerName = "EJ Obiena",
            BookingReference = "PB-20260922-0101",
            BookingDate = new DateOnly(2026, 9, 30),
            Court = new Court { Name = "Court 2" }
        };
        var org = new Organization { Name = "Pole Vault & Pickleball Hub" };

        var msg = BookingTelegramService.BuildBookingCancelledAlertMessage(booking, org);

        Assert.Contains("EJ Obiena", msg);
        Assert.Contains("PB-20260922-0101", msg);
        Assert.Contains("Booking Cancelled", msg);
    }

    [Fact]
    public void SendNewBookingAlertAsync_UsesOrgChatIdWhenAvailable()
    {
        var mockTelegram = new RecordingTelegramService();
        var options = Options.Create(new TelegramOptions { DefaultChatId = "default_chat" });
        var service = new BookingTelegramService(mockTelegram, options, NullLogger<BookingTelegramService>.Instance);

        var org = new Organization { Name = "Club A", TelegramChatId = "-100orgchat" };
        var booking = new Booking { CustomerName = "Test", BookingReference = "PB-1" };

        service.SendNewBookingAlertAsync(booking, org);

        // Allow async fire-and-forget task to execute
        Thread.Sleep(50);

        Assert.Equal("-100orgchat", mockTelegram.LastChatId);
    }

    [Fact]
    public void SendNewBookingAlertAsync_FallsBackToDefaultChatIdWhenOrgChatIdMissing()
    {
        var mockTelegram = new RecordingTelegramService();
        var options = Options.Create(new TelegramOptions { DefaultChatId = "fallback_admin_chat" });
        var service = new BookingTelegramService(mockTelegram, options, NullLogger<BookingTelegramService>.Instance);

        var org = new Organization { Name = "Club B", TelegramChatId = null };
        var booking = new Booking { CustomerName = "Test", BookingReference = "PB-2" };

        service.SendNewBookingAlertAsync(booking, org);

        // Allow async fire-and-forget task to execute
        Thread.Sleep(50);

        Assert.Equal("fallback_admin_chat", mockTelegram.LastChatId);
    }

    // ─── Test Helpers ────────────────────────────────────────────────────────

    private class RecordingTelegramService : ITelegramService
    {
        public string? LastChatId { get; private set; }
        public string? LastMessage { get; private set; }

        public Task<bool> SendMessageAsync(string chatId, string message, CancellationToken ct = default)
        {
            LastChatId = chatId;
            LastMessage = message;
            return Task.FromResult(true);
        }
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseContent;

        public int CallCount { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public string? LastRequestBody { get; private set; }

        public MockHttpMessageHandler(HttpStatusCode statusCode, string responseContent)
        {
            _statusCode = statusCode;
            _responseContent = responseContent;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri;
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
