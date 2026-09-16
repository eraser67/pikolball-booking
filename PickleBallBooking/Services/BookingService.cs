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

    public async Task<BookingResult> CreateBookingWithSlotsAsync(
        int courtId,
        DateOnly bookingDate,
        List<int> timeSlotIds,
        string customerName,
        string customerPhone,
        string customerEmail)
    {
        // Validate input
        if (timeSlotIds == null || timeSlotIds.Count == 0)
        {
            return BookingResult.Fail("At least one TimeSlot must be selected.");
        }

        // Check past date
        if (bookingDate < DateOnly.FromDateTime(DateTime.UtcNow.Date))
        {
            return BookingResult.Fail("Booking date cannot be in the past.");
        }

        // Validate court is active
        var court = await _context.Courts.FindAsync(courtId);
        if (court is null || court.Status != CourtStatus.Active)
        {
            return BookingResult.Fail("The selected court is not available.");
        }

        // Validate continuous slots
        var (isValid, errorMsg) = await ValidateContinuousSlotsAsync(timeSlotIds);
        if (!isValid)
        {
            return BookingResult.Fail(errorMsg);
        }

        // Get the TimeSlots to build StartTime and EndTime
        var slots = await _context.TimeSlots
            .Where(ts => timeSlotIds.Contains(ts.Id))
            .OrderBy(ts => ts.StartTime)
            .ToListAsync();

        var startTime = slots.First().StartTime;
        var endTime = slots.Last().EndTime;
        var durationHours = slots.Count; // Each slot is 1 hour

        // Validate pricing for the time range
        var priceResult = await ValidateAndGetPriceAsync(courtId, bookingDate, startTime, endTime);
        if (!priceResult.Success)
        {
            return BookingResult.Fail(priceResult.ErrorMessage!);
        }

        // Use a transaction to ensure atomicity with database constraint
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            const int maxAttempts = 5;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                // Generate booking reference
                var countForDate = await _context.Bookings.CountAsync(b => b.BookingDate == bookingDate);
                var sequence = countForDate + 1 + attempt;
                var reference = $"PB-{bookingDate:yyyyMMdd}-{sequence:D4}";

                // Create the Booking record
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
                    // Save to get the Booking ID
                    await _context.SaveChangesAsync();

                    // Now create BookingTimeSlot records for each selected slot
                    var slotOrder = 0;
                    foreach (var slotId in timeSlotIds)
                    {
                        var bookingTimeSlot = new BookingTimeSlot
                        {
                            BookingId = booking.Id,
                            CourtId = courtId,
                            BookingDate = bookingDate,
                            TimeSlotId = slotId,
                            SlotOrder = slotOrder++,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };

                        _context.BookingTimeSlots.Add(bookingTimeSlot);
                    }

                    // Save BookingTimeSlot records
                    // Database constraint will enforce unique (CourtId, BookingDate, TimeSlotId) WHERE IsActive=true
                    await _context.SaveChangesAsync();

                    // Commit transaction
                    await transaction.CommitAsync();

                    return BookingResult.Ok(booking);
                }
                catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex, "IX_Bookings_BookingReference"))
                {
                    // Reference collision, try again with next sequence
                    _context.Entry(booking).State = EntityState.Detached;
                }
                catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex, "IX_BookingTimeSlot_CourtId_BookingDate_TimeSlotId_Active"))
                {
                    // Slot already booked - double-booking protection triggered
                    await transaction.RollbackAsync();
                    return BookingResult.Fail("One or more selected TimeSlots are no longer available. Please choose another time.");
                }
            }

            await transaction.RollbackAsync();
            return BookingResult.Fail("Unable to create booking after multiple attempts. Please try again.");
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<List<SlotAvailability>> GetAvailableSlotsAsync(int courtId, DateOnly bookingDate)
    {
        // Get all TimeSlots
        var timeSlots = await _context.TimeSlots
            .OrderBy(ts => ts.StartTime)
            .ToListAsync();

        // Get booked slot IDs for this court on this date (IsActive = true)
        var bookedSlotIds = await _context.BookingTimeSlots
            .Where(bts => bts.CourtId == courtId && bts.BookingDate == bookingDate && bts.IsActive)
            .Select(bts => bts.TimeSlotId)
            .ToListAsync();

        // Get maintenance slot data for this court
        var maintenanceSlots = await _context.CourtTimeSlots
            .Where(cts => cts.CourtId == courtId && cts.AvailabilityStatus == CourtTimeSlotStatus.Maintenance)
            .Select(cts => cts.TimeSlotId)
            .ToListAsync();

        // Build availability list
        var result = new List<SlotAvailability>();
        foreach (var slot in timeSlots)
        {
            result.Add(new SlotAvailability
            {
                TimeSlotId = slot.Id,
                StartTime = slot.StartTime,
                EndTime = slot.EndTime,
                IsAvailable = !bookedSlotIds.Contains(slot.Id) && !maintenanceSlots.Contains(slot.Id),
                IsMaintenance = maintenanceSlots.Contains(slot.Id)
            });
        }

        return result;
    }

    public async Task<BookingResult> CancelBookingAsync(int bookingId)
    {
        var booking = await _context.Bookings.FindAsync(bookingId);
        if (booking is null)
        {
            return BookingResult.Fail("Booking not found.");
        }

        if (booking.BookingStatus == BookingStatus.Cancelled)
        {
            return BookingResult.Fail("Booking is already cancelled.");
        }

        if (booking.BookingStatus == BookingStatus.Completed)
        {
            return BookingResult.Fail("Cannot cancel a completed booking.");
        }

        // Set all BookingTimeSlot records to IsActive = false to release slots
        var bookingTimeSlots = await _context.BookingTimeSlots
            .Where(bts => bts.BookingId == bookingId && bts.IsActive)
            .ToListAsync();

        foreach (var bts in bookingTimeSlots)
        {
            bts.IsActive = false;
            bts.UpdatedAt = DateTime.UtcNow;
        }

        // Update booking status
        booking.BookingStatus = BookingStatus.Cancelled;
        booking.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return BookingResult.Ok(booking);
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

        // If transitioning to Cancelled, release all BookingTimeSlot records
        if (newStatus == BookingStatus.Cancelled)
        {
            var bookingTimeSlots = await _context.BookingTimeSlots
                .Where(bts => bts.BookingId == id && bts.IsActive)
                .ToListAsync();

            foreach (var bts in bookingTimeSlots)
            {
                bts.IsActive = false;
                bts.UpdatedAt = DateTime.UtcNow;
            }
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

    public async Task<(bool IsValid, string? ErrorMessage)> ValidateContinuousSlotsAsync(List<int> timeSlotIds)
    {
        if (timeSlotIds == null || timeSlotIds.Count == 0)
        {
            return (false, "At least one TimeSlot must be selected.");
        }

        if (timeSlotIds.Count == 1)
        {
            return (true, null);
        }

        // Get all selected TimeSlots ordered by StartTime
        var slots = await _context.TimeSlots
            .Where(ts => timeSlotIds.Contains(ts.Id))
            .OrderBy(ts => ts.StartTime)
            .ToListAsync();

        if (slots.Count != timeSlotIds.Count)
        {
            return (false, "One or more selected TimeSlots do not exist.");
        }

        // Verify slots are continuous (no gaps)
        for (int i = 1; i < slots.Count; i++)
        {
            if (slots[i].StartTime != slots[i - 1].EndTime)
            {
                return (false, "Selected TimeSlots must be continuous with no gaps.");
            }
        }

        return (true, null);
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
