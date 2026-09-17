using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Data;

/// <summary>
/// Hard resets the database by deleting all data and seeding fresh sample data.
/// This is useful for development/demo purposes. Only use this in development!
/// </summary>
public static class DatabaseResetSeeder
{
    public static async Task ResetAndSeedAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseResetSeeder));

        try
        {
            logger.LogInformation("Starting database reset and seeding...");

                        // Delete all data in the correct order (respecting foreign keys)
            logger.LogInformation("Clearing existing data...");
            context.BookingTimeSlots.RemoveRange(context.BookingTimeSlots);
            context.CourtTimeSlots.RemoveRange(context.CourtTimeSlots);
            context.Bookings.RemoveRange(context.Bookings);
            context.Pricings.RemoveRange(context.Pricings);
            context.TimeSlots.RemoveRange(context.TimeSlots);
            context.Courts.RemoveRange(context.Courts);
            await context.SaveChangesAsync();

            logger.LogInformation("Adding fresh courts...");
            var courts = new List<Court>
            {
                new()
                {
                    Name = "Court 1 - Indoor Premium",
                    Description = "Indoor court with AC, premium equipment, excellent lighting.",
                    Status = CourtStatus.Active
                },
                new()
                {
                    Name = "Court 2 - Indoor Standard",
                    Description = "Indoor court, standard equipment, good for practice.",
                    Status = CourtStatus.Active
                },
                new()
                {
                    Name = "Court 3 - Outdoor Covered",
                    Description = "Outdoor court with cover, ideal for morning games.",
                    Status = CourtStatus.Active
                },
                new()
                {
                    Name = "Court 4 - Outdoor Open",
                    Description = "Outdoor open court, great for tournaments and large groups.",
                    Status = CourtStatus.Active
                },
                new()
                {
                    Name = "Court 5 - Practice Court",
                    Description = "Smaller practice court, currently under maintenance.",
                    Status = CourtStatus.Inactive
                }
            };
            context.Courts.AddRange(courts);

            logger.LogInformation("Adding fresh time slots...");
            var timeSlots = new List<TimeSlot>
            {
                // Morning slots (6 AM - 9 AM)
                new() { StartTime = new TimeSpan(6, 0, 0), EndTime = new TimeSpan(7, 0, 0), Status = TimeSlotStatus.Active },
                new() { StartTime = new TimeSpan(7, 0, 0), EndTime = new TimeSpan(8, 0, 0), Status = TimeSlotStatus.Active },
                new() { StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(9, 0, 0), Status = TimeSlotStatus.Active },

                // Midday slots (12 PM - 2 PM)
                new() { StartTime = new TimeSpan(12, 0, 0), EndTime = new TimeSpan(13, 0, 0), Status = TimeSlotStatus.Active },
                new() { StartTime = new TimeSpan(13, 0, 0), EndTime = new TimeSpan(14, 0, 0), Status = TimeSlotStatus.Active },

                // Evening slots (5 PM - 9 PM)
                new() { StartTime = new TimeSpan(17, 0, 0), EndTime = new TimeSpan(18, 0, 0), Status = TimeSlotStatus.Active },
                new() { StartTime = new TimeSpan(18, 0, 0), EndTime = new TimeSpan(19, 0, 0), Status = TimeSlotStatus.Active },
                new() { StartTime = new TimeSpan(19, 0, 0), EndTime = new TimeSpan(20, 0, 0), Status = TimeSlotStatus.Active },
                new() { StartTime = new TimeSpan(20, 0, 0), EndTime = new TimeSpan(21, 0, 0), Status = TimeSlotStatus.Active }
            };
            context.TimeSlots.AddRange(timeSlots);

            logger.LogInformation("Adding fresh pricing...");
            var pricings = new List<Pricing>
            {
                // Weekday pricing
                new()
                {
                    DayType = DayType.Weekday,
                    StartTime = new TimeSpan(6, 0, 0),
                    EndTime = new TimeSpan(9, 0, 0),
                    Price = 250m,
                    Status = PricingStatus.Active
                },
                new()
                {
                    DayType = DayType.Weekday,
                    StartTime = new TimeSpan(12, 0, 0),
                    EndTime = new TimeSpan(14, 0, 0),
                    Price = 300m,
                    Status = PricingStatus.Active
                },
                new()
                {
                    DayType = DayType.Weekday,
                    StartTime = new TimeSpan(17, 0, 0),
                    EndTime = new TimeSpan(21, 0, 0),
                    Price = 350m,
                    Status = PricingStatus.Active
                },

                // Weekend pricing
                new()
                {
                    DayType = DayType.Weekend,
                    StartTime = new TimeSpan(6, 0, 0),
                    EndTime = new TimeSpan(9, 0, 0),
                    Price = 300m,
                    Status = PricingStatus.Active
                },
                new()
                {
                    DayType = DayType.Weekend,
                    StartTime = new TimeSpan(12, 0, 0),
                    EndTime = new TimeSpan(14, 0, 0),
                    Price = 350m,
                    Status = PricingStatus.Active
                },
                new()
                {
                    DayType = DayType.Weekend,
                    StartTime = new TimeSpan(17, 0, 0),
                    EndTime = new TimeSpan(21, 0, 0),
                    Price = 400m,
                    Status = PricingStatus.Active
                }
            };
            context.Pricings.AddRange(pricings);

            await context.SaveChangesAsync();

                        logger.LogInformation("Adding fresh sample bookings...");
            var activeCourts = courts.Where(c => c.Status == CourtStatus.Active).ToList();
            var today = AppClock.TodayLocal;

            // Create varied bookings for realistic demo data
            var bookingData = new (int CourtIndex, int TimeSlotIndex, int DayOffset, string Name, string Phone, string Email, BookingStatus Status)[]
            {
                // Today's bookings
                (0, 0, 0, "Maria Santos", "09171234567", "maria.santos@example.com", BookingStatus.Confirmed),
                (1, 1, 0, "Juan Dela Cruz", "09181234567", "juan.delacruz@example.com", BookingStatus.Confirmed),
                (2, 2, 0, "Ana Reyes", "09191234567", "ana.reyes@example.com", BookingStatus.Confirmed),
                (0, 5, 0, "Carlos Bautista", "09201234567", "carlos.bautista@example.com", BookingStatus.Pending),
                (1, 6, 0, "Liza Gomez", "09211234567", "liza.gomez@example.com", BookingStatus.Confirmed),

                // Tomorrow's bookings
                (0, 1, 1, "Pedro Villanueva", "09221234567", "pedro.villanueva@example.com", BookingStatus.Confirmed),
                (1, 2, 1, "Rosa Santos", "09231234567", "rosa.santos@example.com", BookingStatus.Confirmed),
                (3, 6, 1, "Miguel Reyes", "09241234567", "miguel.reyes@example.com", BookingStatus.Pending),
                (0, 3, 1, "Angela Bautista", "09251234567", "angela.bautista@example.com", BookingStatus.Confirmed),

                // Day after tomorrow
                (2, 0, 2, "Robert Villanueva", "09261234567", "robert.villanueva@example.com", BookingStatus.Confirmed),
                (3, 7, 2, "Teresa Gomez", "09271234567", "teresa.gomez@example.com", BookingStatus.Confirmed),
                (1, 4, 2, "Antonio Santos", "09281234567", "antonio.santos@example.com", BookingStatus.Pending),

                // 4 days ahead
                (0, 5, 4, "Elizabeth Reyes", "09291234567", "elizabeth.reyes@example.com", BookingStatus.Confirmed),
                (2, 8, 4, "Vincent Bautista", "09301234567", "vincent.bautista@example.com", BookingStatus.Confirmed),

                // Past bookings (completed)
                (1, 1, -1, "Patricia Gomez", "09311234567", "patricia.gomez@example.com", BookingStatus.Completed),
                (0, 2, -1, "Leo Villanueva", "09321234567", "leo.villanueva@example.com", BookingStatus.Completed),

                // Cancelled bookings
                (3, 5, 3, "Sophia Santos", "09331234567", "sophia.santos@example.com", BookingStatus.Cancelled)
            };

                        var sequence = 0;
            var bookingSlots = new List<BookingTimeSlot>();
            var bookings = bookingData.Select(data =>
            {
                var bookingDate = today.AddDays(data.DayOffset);
                var timeSlot = timeSlots[data.TimeSlotIndex];
                var pricePerHour = CalculatePrice(pricings, bookingDate, timeSlot);

                var booking = new Booking
                {
                    BookingReference = $"PB-{bookingDate:yyyyMMdd}-{++sequence:D4}",
                    CustomerName = data.Name,
                    CustomerPhone = data.Phone,
                    CustomerEmail = data.Email,
                    CourtId = activeCourts[data.CourtIndex].Id,
                    BookingDate = bookingDate,
                    StartTime = timeSlot.StartTime,
                    EndTime = timeSlot.EndTime,
                    DurationHours = (decimal)(timeSlot.EndTime - timeSlot.StartTime).TotalHours,
                    Price = pricePerHour,
                    BookingStatus = data.Status,
                    CreatedAt = DateTime.UtcNow.AddHours(-24),
                    UpdatedAt = DateTime.UtcNow
                };

                // Only active (non-cancelled) bookings hold slot rows; cancelled ones release them.
                if (data.Status != BookingStatus.Cancelled)
                {
                    bookingSlots.Add(new BookingTimeSlot
                    {
                        Booking = booking,
                        CourtId = booking.CourtId,
                        BookingDate = bookingDate,
                        TimeSlotId = timeSlot.Id,
                        SlotOrder = 0,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow.AddHours(-24),
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                return booking;
            }).ToList();

            context.Bookings.AddRange(bookings);
            context.BookingTimeSlots.AddRange(bookingSlots);
            await context.SaveChangesAsync();

            logger.LogInformation("✅ Database reset complete! Added:");
            logger.LogInformation($"   - {courts.Count} courts (4 active, 1 inactive)");
            logger.LogInformation($"   - {timeSlots.Count} time slots");
            logger.LogInformation($"   - {pricings.Count} pricing rules");
            logger.LogInformation($"   - {bookings.Count} sample bookings");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during database reset and seeding");
            throw;
        }
    }

    private static decimal CalculatePrice(List<Pricing> pricings, DateOnly date, TimeSlot timeSlot)
    {
        var dayType = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday
            ? DayType.Weekend
            : DayType.Weekday;

        var pricing = pricings.FirstOrDefault(p =>
            p.Status == PricingStatus.Active &&
            p.DayType == dayType &&
            p.StartTime <= timeSlot.StartTime &&
            timeSlot.EndTime <= p.EndTime);

        return pricing?.Price ?? 250m;
    }
}
