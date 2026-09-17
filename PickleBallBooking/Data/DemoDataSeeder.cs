using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Data;

/// <summary>
/// Seeds sample courts, time slots, pricing, and bookings so the application has
/// realistic data to view during development/demos. Only runs when the database
/// has no courts yet, so it is safe to leave in place and won't duplicate data
/// or overwrite real records on subsequent runs.
/// </summary>
public static class DemoDataSeeder
{
    public static async Task SeedAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (await context.Courts.AnyAsync())
        {
            // Demo data already present (or real data exists) - do nothing.
            return;
        }

        var courts = new List<Court>
        {
            new() { Name = "Court 1", Description = "Indoor court near the main entrance.", Status = CourtStatus.Active },
            new() { Name = "Court 2", Description = "Indoor court with extra spectator seating.", Status = CourtStatus.Active },
            new() { Name = "Court 3", Description = "Outdoor court, covered.", Status = CourtStatus.Inactive }
        };
        context.Courts.AddRange(courts);

        var timeSlots = new List<TimeSlot>
        {
            new() { StartTime = new TimeSpan(6, 0, 0), EndTime = new TimeSpan(7, 0, 0), Status = TimeSlotStatus.Active },
            new() { StartTime = new TimeSpan(7, 0, 0), EndTime = new TimeSpan(8, 0, 0), Status = TimeSlotStatus.Active },
            new() { StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(9, 0, 0), Status = TimeSlotStatus.Active },
            new() { StartTime = new TimeSpan(17, 0, 0), EndTime = new TimeSpan(18, 0, 0), Status = TimeSlotStatus.Active },
            new() { StartTime = new TimeSpan(18, 0, 0), EndTime = new TimeSpan(19, 0, 0), Status = TimeSlotStatus.Active },
            new() { StartTime = new TimeSpan(19, 0, 0), EndTime = new TimeSpan(20, 0, 0), Status = TimeSlotStatus.Inactive }
        };
        context.TimeSlots.AddRange(timeSlots);

        var pricings = new List<Pricing>
        {
            new() { DayType = DayType.Weekday, StartTime = new TimeSpan(6, 0, 0), EndTime = new TimeSpan(9, 0, 0), Price = 250m, Status = PricingStatus.Active },
            new() { DayType = DayType.Weekday, StartTime = new TimeSpan(17, 0, 0), EndTime = new TimeSpan(20, 0, 0), Price = 350m, Status = PricingStatus.Active },
            new() { DayType = DayType.Weekend, StartTime = new TimeSpan(6, 0, 0), EndTime = new TimeSpan(9, 0, 0), Price = 300m, Status = PricingStatus.Active },
            new() { DayType = DayType.Weekend, StartTime = new TimeSpan(17, 0, 0), EndTime = new TimeSpan(20, 0, 0), Price = 400m, Status = PricingStatus.Active }
        };
        context.Pricings.AddRange(pricings);

        await context.SaveChangesAsync();

                var activeCourts = courts.Where(c => c.Status == CourtStatus.Active).ToList();
        var today = AppClock.TodayLocal;

        var sampleBookings = new (Court Court, TimeSlot TimeSlot, DateOnly Date, string Name, string Phone, string Email, BookingStatus Status, decimal Price)[]
        {
            (activeCourts[0], timeSlots[0], today, "Maria Santos", "09171234567", "maria.santos@example.com", BookingStatus.Pending, 250m),
            (activeCourts[1], timeSlots[1], today, "Juan Dela Cruz", "09181234567", "juan.delacruz@example.com", BookingStatus.Confirmed, 250m),
            (activeCourts[0], timeSlots[3], today, "Ana Reyes", "09191234567", "ana.reyes@example.com", BookingStatus.Confirmed, 350m),
            (activeCourts[1], timeSlots[4], today, "Carlos Bautista", "09201234567", "carlos.bautista@example.com", BookingStatus.Cancelled, 350m),
            (activeCourts[0], timeSlots[1], today.AddDays(-1), "Liza Gomez", "09211234567", "liza.gomez@example.com", BookingStatus.Completed, 250m),
            (activeCourts[1], timeSlots[2], today.AddDays(1), "Pedro Villanueva", "09221234567", "pedro.villanueva@example.com", BookingStatus.Pending, 250m)
        };

        var sequence = 1;
        var bookingSlots = new List<BookingTimeSlot>();
        var bookings = sampleBookings.Select(sample =>
        {
            var booking = new Booking
            {
                BookingReference = $"PB-{sample.Date:yyyyMMdd}-{sequence++:D4}",
                CustomerName = sample.Name,
                CustomerPhone = sample.Phone,
                CustomerEmail = sample.Email,
                CourtId = sample.Court.Id,
                BookingDate = sample.Date,
                StartTime = sample.TimeSlot.StartTime,
                EndTime = sample.TimeSlot.EndTime,
                DurationHours = (decimal)(sample.TimeSlot.EndTime - sample.TimeSlot.StartTime).TotalHours,
                Price = sample.Price,
                BookingStatus = sample.Status,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Only active (non-cancelled) bookings hold slot rows; cancelled ones release them.
            if (sample.Status != BookingStatus.Cancelled)
            {
                bookingSlots.Add(new BookingTimeSlot
                {
                    Booking = booking,
                    CourtId = sample.Court.Id,
                    BookingDate = sample.Date,
                    TimeSlotId = sample.TimeSlot.Id,
                    SlotOrder = 0,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            return booking;
        }).ToList();

        context.Bookings.AddRange(bookings);
        context.BookingTimeSlots.AddRange(bookingSlots);
        await context.SaveChangesAsync();
    }
}
