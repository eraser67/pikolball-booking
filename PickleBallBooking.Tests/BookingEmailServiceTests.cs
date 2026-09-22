using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PickleBallBooking.Models;
using PickleBallBooking.Services;
using Xunit;

namespace PickleBallBooking.Tests;

public class BookingEmailServiceTests
{
    private sealed class EmailServiceSpy : IEmailService
    {
        public string? LastToAddress { get; private set; }
        public string? LastToName { get; private set; }
        public string? LastSubject { get; private set; }
        public string? LastHtmlBody { get; private set; }
        public string? LastFromAddress { get; private set; }
        public string? LastFromName { get; private set; }
        public string? LastReplyTo { get; private set; }
        public int SendCount { get; private set; }

        public Task SendAsync(
            string toAddress,
            string toName,
            string subject,
            string htmlBody,
            string? fromAddress = null,
            string? fromName = null,
            string? replyTo = null,
            CancellationToken ct = default)
        {
            SendCount++;
            LastToAddress = toAddress;
            LastToName = toName;
            LastSubject = subject;
            LastHtmlBody = htmlBody;
            LastFromAddress = fromAddress;
            LastFromName = fromName;
            LastReplyTo = replyTo;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task SendBookingReceivedAsync_UsesOrgNameAndSlugAsSender()
    {
        // Arrange
        var spy = new EmailServiceSpy();
        var options = Options.Create(new EmailOptions
        {
            Enabled = true,
            FromAddress = "noreply@punitbola.tech",
            FromName = "Pikolball Booking"
        });
        var service = new BookingEmailService(spy, NullLogger<BookingEmailService>.Instance, options);

        var org = new Organization
        {
            Id = 1,
            Name = "Demo Pickleball Club",
            Slug = "demo",
            NotificationEmail = "admin@demo.com"
        };

        var booking = new Booking
        {
            BookingReference = "PB-TEST1234",
            CustomerName = "Jane Doe",
            CustomerEmail = "jane@example.com",
            BookingDate = new DateOnly(2026, 9, 25),
            StartTime = TimeSpan.FromHours(18),
            EndTime = TimeSpan.FromHours(19),
            Price = 500m
        };

        // Act
        service.SendBookingReceivedAsync(booking, org);
        await Task.Delay(50); // allow Task.Run / fire-and-forget completion

        // Assert
        Assert.Equal(1, spy.SendCount);
        Assert.Equal("jane@example.com", spy.LastToAddress);
        Assert.Equal("Jane Doe", spy.LastToName);
        Assert.Equal("demo@punitbola.tech", spy.LastFromAddress);
        Assert.Equal("Demo Pickleball Club", spy.LastFromName);
        Assert.Equal("admin@demo.com", spy.LastReplyTo);
    }

    [Fact]
    public async Task SendBookingReceivedAsync_SanitizesSpecialCharactersInSlug()
    {
        // Arrange
        var spy = new EmailServiceSpy();
        var options = Options.Create(new EmailOptions
        {
            Enabled = true,
            FromAddress = "system@punitbola.tech",
            FromName = "Pikolball Booking"
        });
        var service = new BookingEmailService(spy, NullLogger<BookingEmailService>.Instance, options);

        var org = new Organization
        {
            Id = 2,
            Name = "Metro Pickleball!",
            Slug = "Metro_Pickleball#123",
            NotificationEmail = "metro@club.com"
        };

        var booking = new Booking
        {
            BookingReference = "PB-TEST5678",
            CustomerName = "John Smith",
            CustomerEmail = "john@example.com",
            BookingDate = new DateOnly(2026, 9, 25),
            StartTime = TimeSpan.FromHours(10),
            EndTime = TimeSpan.FromHours(11),
            Price = 300m
        };

        // Act
        service.SendBookingReceivedAsync(booking, org);
        await Task.Delay(50);

        // Assert
        Assert.Equal("metropickleball123@punitbola.tech", spy.LastFromAddress);
        Assert.Equal("Metro Pickleball!", spy.LastFromName);
        Assert.Equal("metro@club.com", spy.LastReplyTo);
    }

    [Fact]
    public async Task SendNewBookingToOrgAsync_SetsReplyToAsCustomerEmail()
    {
        // Arrange
        var spy = new EmailServiceSpy();
        var options = Options.Create(new EmailOptions
        {
            Enabled = true,
            FromAddress = "noreply@punitbola.tech"
        });
        var service = new BookingEmailService(spy, NullLogger<BookingEmailService>.Instance, options);

        var org = new Organization
        {
            Id = 1,
            Name = "Demo Pickleball Club",
            Slug = "demo",
            NotificationEmail = "owner@demo.com"
        };

        var booking = new Booking
        {
            BookingReference = "PB-TEST9999",
            CustomerName = "Alice Player",
            CustomerEmail = "alice@gmail.com",
            BookingDate = new DateOnly(2026, 9, 25),
            StartTime = TimeSpan.FromHours(14),
            EndTime = TimeSpan.FromHours(16),
            Price = 800m
        };

        // Act
        service.SendNewBookingToOrgAsync(booking, org);
        await Task.Delay(50);

        // Assert
        Assert.Equal(1, spy.SendCount);
        Assert.Equal("owner@demo.com", spy.LastToAddress);
        Assert.Equal("demo@punitbola.tech", spy.LastFromAddress);
        Assert.Equal("Demo Pickleball Club", spy.LastFromName);
        Assert.Equal("alice@gmail.com", spy.LastReplyTo);
    }
}
