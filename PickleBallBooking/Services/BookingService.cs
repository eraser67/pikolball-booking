using Microsoft.EntityFrameworkCore;
using Npgsql;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public class BookingService : IBookingService
{
    private readonly ApplicationDbContext _context;

    public BookingService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> IsAvailableAsync(int courtId, int timeSlotId, DateOnly bookingDate)
    {
        var alreadyBooked = await _context.Bookings.AnyAsync(b =>
            b.CourtId == courtId
            && b.TimeSlotId == timeSlotId
            && b.BookingDate == bookingDate
            && b.BookingStatus != BookingStatus.Cancelled);

        return !alreadyBooked;
    }

    public async Task<Models.Booking?> LookupBookingAsync(string bookingReference, string contactInfo)
    {
        if (string.IsNullOrWhiteSpace(bookingReference) || string.IsNullOrWhiteSpace(contactInfo))
        {
            return null;
        }

        var reference = bookingReference.Trim();
        var contact = contactInfo.Trim();

        return await _context.Bookings
            .Include(b => b.Court)
            .Include(b => b.TimeSlot)
            .FirstOrDefaultAsync(b =>
                b.BookingReference == reference
                && (b.CustomerPhone == contact || b.CustomerEmail.ToLower() == contact.ToLower()));
    }

    public async Task<PriceCalculationResult> CalculatePriceAsync(int courtId, int timeSlotId, DateOnly bookingDate)
    {
        var validation = await ValidateAndGetPriceAsync(courtId, timeSlotId, bookingDate);
        return validation;
    }

    public async Task<BookingResult> CreateBookingAsync(
        int courtId,
        int timeSlotId,
        DateOnly bookingDate,
        string customerName,
        string customerPhone,
        string customerEmail)
    {
        var priceResult = await ValidateAndGetPriceAsync(courtId, timeSlotId, bookingDate);
        if (!priceResult.Success)
        {
            return BookingResult.Fail(priceResult.ErrorMessage!);
        }

        const int maxAttempts = 5;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            // Re-validate availability on every attempt: the slot may have just been
            // booked by another customer between the initial check and this attempt.
            var isAvailable = await IsAvailableAsync(courtId, timeSlotId, bookingDate);
            if (!isAvailable)
            {
                return BookingResult.Fail("Sorry, this time slot is no longer available. Please select another time.");
            }

            var countForDate = await _context.Bookings.CountAsync(b => b.BookingDate == bookingDate);
            var sequence = countForDate + 1 + attempt;
            var reference = $"PB-{bookingDate:yyyyMMdd}-{sequence:D4}";

            var booking = new Booking
            {
                BookingReference = reference,
                CustomerName = customerName,
                CustomerPhone = customerPhone,
                CustomerEmail = customerEmail,
                CourtId = courtId,
                BookingDate = bookingDate,
                TimeSlotId = timeSlotId,
                Price = priceResult.Price,
                BookingStatus = BookingStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Bookings.Add(booking);

            try
            {
                await _context.SaveChangesAsync();
                return BookingResult.Ok(booking);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex, "IX_Bookings_CourtId_BookingDate_TimeSlotId"))
            {
                // Another request won the race and booked this slot first (database-level
                // double booking protection). Do not retry with a new reference; the slot
                // is genuinely taken.
                _context.Entry(booking).State = EntityState.Detached;
                return BookingResult.Fail("Sorry, this time slot is no longer available. Please select another time.");
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex, "IX_Bookings_BookingReference"))
            {
                // Extremely unlikely reference collision: retry with a freshly computed reference.
                _context.Entry(booking).State = EntityState.Detached;
            }
        }

        return BookingResult.Fail("Sorry, this time slot is no longer available. Please select another time.");
    }

    public async Task<List<Booking>> GetBookingsForAdminAsync(BookingAdminFilter filter)
    {
        var query = _context.Bookings
            .Include(b => b.Court)
            .Include(b => b.TimeSlot)
            .AsQueryable();

        if (filter.BookingDate.HasValue)
        {
            query = query.Where(b => b.BookingDate == filter.BookingDate.Value);
        }

        if (filter.CourtId.HasValue)
        {
            query = query.Where(b => b.CourtId == filter.CourtId.Value);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(b => b.BookingStatus == filter.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.CustomerSearch))
        {
            var search = filter.CustomerSearch.Trim().ToLower();
            query = query.Where(b =>
                b.CustomerName.ToLower().Contains(search)
                || b.CustomerPhone.ToLower().Contains(search)
                || b.CustomerEmail.ToLower().Contains(search)
                || b.BookingReference.ToLower().Contains(search));
        }

        return await query
            .OrderByDescending(b => b.BookingDate)
            .ThenBy(b => b.TimeSlot!.StartTime)
            .ToListAsync();
    }

    public async Task<Booking?> GetBookingByIdAsync(int id)
    {
        return await _context.Bookings
            .Include(b => b.Court)
            .Include(b => b.TimeSlot)
            .FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<BookingResult> UpdateBookingStatusAsync(int id, BookingStatus newStatus)
    {
        var booking = await _context.Bookings.FindAsync(id);
        if (booking is null)
        {
            return BookingResult.Fail("Booking not found.");
        }

        if (!IsValidStatusTransition(booking.BookingStatus, newStatus))
        {
            return BookingResult.Fail($"Cannot change booking status from {booking.BookingStatus} to {newStatus}.");
        }

        booking.BookingStatus = newStatus;
        booking.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return BookingResult.Ok(booking);
    }

    private static bool IsValidStatusTransition(BookingStatus current, BookingStatus next)
    {
        if (current == next)
        {
            return false;
        }

        return current switch
        {
            BookingStatus.Pending => next is BookingStatus.Confirmed or BookingStatus.Cancelled,
            BookingStatus.Confirmed => next is BookingStatus.Completed or BookingStatus.Cancelled,
            BookingStatus.Cancelled => false,
            BookingStatus.Completed => false,
            _ => false
        };
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex, string indexName)
    {
        return ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgresException
            && postgresException.ConstraintName == indexName;
    }

    private async Task<PriceCalculationResult> ValidateAndGetPriceAsync(int courtId, int timeSlotId, DateOnly bookingDate)
    {
        if (bookingDate < DateOnly.FromDateTime(DateTime.UtcNow.Date))
        {
            return PriceCalculationResult.Fail("Booking date cannot be in the past.");
        }

        var court = await _context.Courts.FindAsync(courtId);
        if (court is null || court.Status != CourtStatus.Active)
        {
            return PriceCalculationResult.Fail("The selected court is not available.");
        }

        var timeSlot = await _context.TimeSlots.FindAsync(timeSlotId);
        if (timeSlot is null || timeSlot.Status != TimeSlotStatus.Active)
        {
            return PriceCalculationResult.Fail("The selected time slot is not available.");
        }

        var dayType = bookingDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? DayType.Weekend
            : DayType.Weekday;

        var pricing = await _context.Pricings
            .Where(p => p.Status == PricingStatus.Active
                        && p.DayType == dayType
                        && p.StartTime <= timeSlot.StartTime
                        && p.EndTime >= timeSlot.EndTime)
            .FirstOrDefaultAsync();

        if (pricing is null)
        {
            return PriceCalculationResult.Fail("Pricing is not configured for the selected date and time.");
        }

        var isAvailable = await IsAvailableAsync(courtId, timeSlotId, bookingDate);
        if (!isAvailable)
        {
            return PriceCalculationResult.Fail("Sorry, this time slot is no longer available. Please select another time.");
        }

        return PriceCalculationResult.Ok(pricing.Price);
    }
}
