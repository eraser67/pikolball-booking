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

    public async Task<bool> IsAvailableAsync(int courtId, DateOnly bookingDate, TimeSpan startTime, TimeSpan endTime)
    {
        var alreadyBooked = await _context.Bookings.AnyAsync(b =>
            b.CourtId == courtId
            && b.BookingDate == bookingDate
            && b.BookingStatus != BookingStatus.Cancelled
            && b.StartTime < endTime
            && b.EndTime > startTime);

        return !alreadyBooked;
    }

    public async Task<Booking?> LookupBookingAsync(string bookingReference, string contactInfo)
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

    public async Task<PriceCalculationResult> CalculatePriceAsync(DateOnly bookingDate, TimeSpan startTime, TimeSpan endTime)
    {
        var validation = await ValidateAndGetPriceAsync(null, bookingDate, startTime, endTime);
        return validation;
    }

    public async Task<BookingResult> CreateBookingAsync(
        int courtId,
        DateOnly bookingDate,
        TimeSpan startTime,
        TimeSpan endTime,
        string customerName,
        string customerPhone,
        string customerEmail)
    {
        var priceResult = await ValidateAndGetPriceAsync(courtId, bookingDate, startTime, endTime);
        if (!priceResult.Success)
        {
            return BookingResult.Fail(priceResult.ErrorMessage!);
        }

        const int maxAttempts = 5;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var isAvailable = await IsAvailableAsync(courtId, bookingDate, startTime, endTime);
            if (!isAvailable)
            {
                return BookingResult.Fail("Sorry, this time range is no longer available. Please select another time.");
            }

            var countForDate = await _context.Bookings.CountAsync(b => b.BookingDate == bookingDate);
            var sequence = countForDate + 1 + attempt;
            var reference = $"PB-{bookingDate:yyyyMMdd}-{sequence:D4}";

            var durationHours = Math.Round((endTime - startTime).TotalHours, 2);
            var booking = new Booking
            {
                BookingReference = reference,
                CustomerName = customerName,
                CustomerPhone = customerPhone,
                CustomerEmail = customerEmail,
                CourtId = courtId,
                BookingDate = bookingDate,
                StartTime = startTime,
                EndTime = endTime,
                DurationHours = (decimal)durationHours,
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
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex, "IX_Bookings_BookingReference"))
            {
                _context.Entry(booking).State = EntityState.Detached;
            }
            catch (DbUpdateException ex) when (IsExclusionConstraintViolation(ex, "EX_Bookings_NoOverlap"))
            {
                _context.Entry(booking).State = EntityState.Detached;
                return BookingResult.Fail("The selected time is no longer available. Please choose another time.");
            }
        }

        return BookingResult.Fail("The selected time is no longer available. Please choose another time.");
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
            .ThenBy(b => b.StartTime)
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

    private static bool IsExclusionConstraintViolation(DbUpdateException ex, string constraintName)
    {
        return ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation } postgresException
            && postgresException.ConstraintName == constraintName;
    }

    private async Task<PriceCalculationResult> ValidateAndGetPriceAsync(int? courtId, DateOnly bookingDate, TimeSpan startTime, TimeSpan endTime)
    {
        if (bookingDate < DateOnly.FromDateTime(DateTime.UtcNow.Date))
        {
            return PriceCalculationResult.Fail("Booking date cannot be in the past.");
        }

        if (endTime <= startTime)
        {
            return PriceCalculationResult.Fail("End time must be after start time.");
        }

        var durationHours = (endTime - startTime).TotalHours;
        if (durationHours <= 0)
        {
            return PriceCalculationResult.Fail("Booking duration must be greater than zero.");
        }

        if (courtId.HasValue)
        {
            var court = await _context.Courts.FindAsync(courtId.Value);
            if (court is null || court.Status != CourtStatus.Active)
            {
                return PriceCalculationResult.Fail("The selected court is not available.");
            }
        }

        var dayType = bookingDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? DayType.Weekend
            : DayType.Weekday;

        var pricingBands = await _context.Pricings
            .Where(p => p.Status == PricingStatus.Active && p.DayType == dayType)
            .OrderBy(p => p.StartTime)
            .ToListAsync();

        if (pricingBands.Count == 0)
        {
            return PriceCalculationResult.Fail("Pricing is not configured for the selected date and time.");
        }

        var cursor = startTime;
        decimal totalPrice = 0m;

        while (cursor < endTime)
        {
            var band = pricingBands.FirstOrDefault(p => p.StartTime <= cursor && p.EndTime > cursor);
            if (band is null)
            {
                return PriceCalculationResult.Fail("Pricing is not configured for the selected date and time.");
            }

            var segmentEnd = pricingBands
                .Where(p => p.StartTime > cursor)
                .Select(p => p.StartTime)
                .DefaultIfEmpty(endTime)
                .Min();

            if (segmentEnd > band.EndTime)
            {
                segmentEnd = band.EndTime;
            }

            if (segmentEnd > endTime)
            {
                segmentEnd = endTime;
            }

            if (segmentEnd <= cursor)
            {
                return PriceCalculationResult.Fail("Pricing is not configured for the selected date and time.");
            }

            totalPrice += (decimal)(segmentEnd - cursor).TotalHours * band.Price;
            cursor = segmentEnd;
        }

        if (courtId.HasValue)
        {
            var isAvailable = await IsAvailableAsync(courtId.Value, bookingDate, startTime, endTime);
            if (!isAvailable)
            {
                return PriceCalculationResult.Fail("Sorry, this time range is no longer available. Please select another time.");
            }
        }

        return PriceCalculationResult.Ok(decimal.Round(totalPrice, 2));
    }
}
