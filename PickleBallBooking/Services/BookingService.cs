using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Microsoft.Extensions.Logging;
using PickleBallBooking.Data;
using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public class BookingService : IBookingService
{
    private readonly ApplicationDbContext _context;
    private readonly BookingEmailService? _emailService;
    private readonly BookingSmsService? _smsService;
    private readonly BookingTelegramService? _telegramService;
    private readonly ISubscriptionService? _subscriptionService;
    private readonly ILogger<BookingService>? _logger;

    public BookingService(
        ApplicationDbContext context,
        BookingEmailService? emailService = null,
        ISubscriptionService? subscriptionService = null,
        ILogger<BookingService>? logger = null,
        BookingSmsService? smsService = null,
        BookingTelegramService? telegramService = null)
    {
        _context             = context;
        _emailService        = emailService;
        _subscriptionService = subscriptionService;
        _logger              = logger;
        _smsService          = smsService;
        _telegramService     = telegramService;
    }

    public async Task<bool> IsAvailableAsync(int courtId, DateOnly bookingDate, TimeSpan startTime, TimeSpan endTime)
    {
        var (startHours, endHours) = AppClock.ToAbsoluteRangeNormalized(startTime, endTime);
        if (endHours > 24)
        {
            // Overnight booking crossing midnight:
            // Day 1: from startTime to midnight on bookingDate
            var day1Available = await IsAvailableAsync(courtId, bookingDate, startTime, TimeSpan.Zero);
            if (!day1Available) return false;

            // Day 2: from midnight to endTime on bookingDate.AddDays(1)
            var day2Available = await IsAvailableAsync(courtId, bookingDate.AddDays(1), TimeSpan.Zero, endTime);
            return day2Available;
        }

        // Same-day range
        var effectiveEnd = endTime == TimeSpan.Zero ? TimeSpan.FromHours(24) : endTime;

        // 1. Check BookingTimeSlots (authoritative slot records storing true calendar date per slot)
        var bookedViaTimeSlots = await _context.BookingTimeSlots
            .AnyAsync(bts =>
                bts.CourtId == courtId
                && bts.BookingDate == bookingDate
                && bts.IsActive
                && bts.TimeSlot.StartTime < effectiveEnd
                && (bts.TimeSlot.EndTime == TimeSpan.Zero ? TimeSpan.FromHours(24) : bts.TimeSlot.EndTime) > startTime);

        if (bookedViaTimeSlots)
        {
            return false;
        }

        // 2. Check Bookings table on bookingDate for any overlapping active bookings
        var bookedOnDate = await _context.Bookings.AnyAsync(b =>
            b.CourtId == courtId
            && b.BookingDate == bookingDate
            && b.BookingStatus != BookingStatus.Cancelled
            && b.StartTime < effectiveEnd
            && ((b.EndTime == TimeSpan.Zero || b.EndTime <= b.StartTime) ? TimeSpan.FromHours(24) : b.EndTime) > startTime);

        if (bookedOnDate)
        {
            return false;
        }

        // 3. Check Bookings table on previous day for any active overnight bookings extending past midnight into today
        var prevDate = bookingDate.AddDays(-1);
        var bookedFromPrevDay = await _context.Bookings.AnyAsync(b =>
            b.CourtId == courtId
            && b.BookingDate == prevDate
            && b.BookingStatus != BookingStatus.Cancelled
            && b.EndTime > TimeSpan.Zero
            && b.EndTime <= b.StartTime // Overnight booking crossing midnight into bookingDate
            && b.EndTime > startTime);

        if (bookedFromPrevDay)
        {
            return false;
        }

        // 4. Court-level maintenance for any hour covered by the requested range
        var sameDayMaintenance = await _context.CourtTimeSlots
            .AnyAsync(cts =>
                cts.CourtId == courtId
                && cts.AvailabilityStatus == CourtTimeSlotStatus.Maintenance
                && cts.TimeSlot.StartTime < effectiveEnd
                && (cts.TimeSlot.EndTime == TimeSpan.Zero ? TimeSpan.FromHours(24) : cts.TimeSlot.EndTime) > startTime);

        return !sameDayMaintenance;
    }

    public async Task<Booking?> LookupBookingAsync(string bookingReference, string contactInfo)
    {
        if (string.IsNullOrWhiteSpace(bookingReference) || string.IsNullOrWhiteSpace(contactInfo))
        {
            return null;
        }

                        var reference = bookingReference.Trim();
        var contact = contactInfo.Trim();
        var contactLower = contact.ToLower();

        return await _context.Bookings
            .Include(b => b.Court)
            .Include(b => b.TimeSlots)
                .ThenInclude(bts => bts.TimeSlot)
            .FirstOrDefaultAsync(b =>
                b.BookingReference == reference
                && (b.CustomerPhone == contact || b.CustomerEmail.ToLower() == contactLower));
    }

    public async Task<PriceCalculationResult> CalculatePriceAsync(DateOnly bookingDate, TimeSpan startTime, TimeSpan endTime, int? courtId = null)
    {
        return await ValidateAndGetPriceAsync(courtId, bookingDate, startTime, endTime);
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

                // Check past date (local UTC+8 time)
        if (bookingDate < AppClock.TodayLocal)
        {
            return BookingResult.Fail("Booking date cannot be in the past.");
        }

        // Phase 26: subscription gate — block new bookings for expired/suspended orgs.
        if (_subscriptionService != null && !await _subscriptionService.CanAcceptBookingsAsync())
        {
            return BookingResult.Fail(
                "Bookings are currently unavailable. Please contact the venue to renew their subscription.");
        }

                // Validate court is active
        var court = await _context.Courts.FirstOrDefaultAsync(c => c.Id == courtId);
        if (court is null || court.Status != CourtStatus.Active)
        {
            return BookingResult.Fail("The selected court is not available.");
        }

                // Validate continuous slots
                var (isValid, errorMsg) = await ValidateContinuousSlotsAsync(timeSlotIds);
        if (!isValid)
        {
            return BookingResult.Fail(errorMsg!);
        }

        // Get the TimeSlots to build StartTime and EndTime
        var slots = await _context.TimeSlots
                        .Where(ts => timeSlotIds.Contains(ts.Id))
            .OrderBy(ts => ts.StartTime)
            .ToListAsync();

        if (slots.Count != timeSlotIds.Count)
        {
            return BookingResult.Fail("One or more selected TimeSlots do not exist.");
        }

        // Order slots chronologically across midnight
        var chronologicalSlots = OrderSlotsChronologically(slots);

        // Authoritative "no past slots" rule: for a booking starting today,
        // no slot on today (before midnight) may have already started.
        // Slots after midnight occur on tomorrow (Today + 1 day), which has not started yet.
        if (bookingDate == AppClock.TodayLocal)
        {
            var nowHours = AppClock.NowLocal.TimeOfDay.TotalHours;
            var day1Slots = chronologicalSlots.TakeWhile(s => s.StartTime >= chronologicalSlots.First().StartTime);
            if (day1Slots.Any(s => s.StartTime.TotalHours <= nowHours))
            {
                return BookingResult.Fail("One or more selected time slots have already passed. Please choose a later time.");
            }
        }

        var startTime = chronologicalSlots.First().StartTime;
        var endTime = chronologicalSlots.Last().EndTime;
        var durationHours = chronologicalSlots.Count; // Each slot is 1 hour

        // Validate pricing and availability for the time range
        var priceResult = await ValidateAndGetPriceAsync(courtId, bookingDate, startTime, endTime);
        if (!priceResult.Success)
        {
            return BookingResult.Fail(priceResult.ErrorMessage!);
        }

        // If the caller already owns a transaction (e.g. a test harness or an outer
        // unit of work), participate in it and let the caller decide commit/rollback.
        // A retrying execution strategy (EnableRetryOnFailure) forbids opening a
        // second, user-initiated transaction on the same connection, and would also
        // conflict with the ambient one, so we only manage our own when there is none.
        if (_context.Database.CurrentTransaction is null)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(() => CreateBookingCoreAsync(
                courtId, court.OrganizationId, bookingDate, chronologicalSlots,
                customerName, customerPhone, customerEmail,
                startTime, endTime, durationHours, priceResult.Price,
                manageTransaction: true));
        }

        return await CreateBookingCoreAsync(
            courtId, court.OrganizationId, bookingDate, chronologicalSlots,
            customerName, customerPhone, customerEmail,
            startTime, endTime, durationHours, priceResult.Price,
            manageTransaction: false);
    }

    private async Task<BookingResult> CreateBookingCoreAsync(
        int courtId,
        int courtOrganizationId,
        DateOnly bookingDate,
        List<TimeSlot> slots,
        string customerName,
        string customerPhone,
        string customerEmail,
        TimeSpan startTime,
        TimeSpan endTime,
        int durationHours,
        decimal price,
        bool manageTransaction)
    {
        IDbContextTransaction? transaction = null;
        if (manageTransaction)
        {
            // Use a transaction to ensure atomicity with database constraint
            transaction = await _context.Database.BeginTransactionAsync();
        }

        try
        {
            const int maxAttempts = 10;
            var prefix = $"PB-{bookingDate:yyyyMMdd}-";
            var existingRefs = await _context.Bookings.IgnoreQueryFilters()
                .Where(b => b.BookingDate == bookingDate && b.BookingReference.StartsWith(prefix))
                .Select(b => b.BookingReference)
                .ToListAsync();

            var maxSeq = 0;
            foreach (var r in existingRefs)
            {
                if (r.Length > prefix.Length && int.TryParse(r.Substring(prefix.Length), out var seq))
                {
                    if (seq > maxSeq) maxSeq = seq;
                }
            }

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                // Generate booking reference across all tenants to avoid unique constraint collision
                var sequence = Math.Max(maxSeq, existingRefs.Count) + 1 + attempt;
                var reference = $"{prefix}{sequence:D4}";

                // Phase 20.5: attach the booking to the court's organization so the
                // NOT NULL tenant foreign keys are satisfied. This uses the court's own
                // OrganizationId (no tenant context/filtering yet; the app runs as the
                // single Pikolball tenant).
                var bookingOrganizationId = courtOrganizationId;

                // Create the Booking record
                var booking = new Booking
                {
                    OrganizationId = bookingOrganizationId,
                    BookingReference = reference,
                    CustomerName = customerName,
                    CustomerPhone = customerPhone,
                    CustomerEmail = customerEmail,
                    CourtId = courtId,
                    BookingDate = bookingDate,
                    StartTime = startTime,
                    EndTime = endTime,
                    DurationHours = (decimal)durationHours,
                    Price = price,
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
                    bool crossedMidnight = false;
                    foreach (var slot in slots)
                    {
                        if (slotOrder > 0 && slot.StartTime == TimeSpan.Zero)
                        {
                            crossedMidnight = true;
                        }

                        var slotBookingDate = crossedMidnight ? bookingDate.AddDays(1) : bookingDate;

                        var bookingTimeSlot = new BookingTimeSlot
                        {
                            OrganizationId = bookingOrganizationId,
                            BookingId = booking.Id,
                            CourtId = courtId,
                            BookingDate = slotBookingDate,
                            TimeSlotId = slot.Id,
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

                    // Commit transaction (only when we own it)
                    if (transaction is not null)
                    {
                        await transaction.CommitAsync();
                    }

                    // Phase 26: fire customer + org notifications (email + SMS) after successful booking.
                    try
                    {
                        var orgForEmail = await _context.Organizations
                            .FirstOrDefaultAsync(o => o.Id == bookingOrganizationId);
                        if (orgForEmail is not null)
                        {
                            booking.Court = await _context.Courts.FindAsync(courtId);
                            _emailService?.SendBookingReceivedAsync(booking, orgForEmail);
                            _emailService?.SendNewBookingToOrgAsync(booking, orgForEmail);
                            _smsService?.SendBookingReceivedAsync(booking, orgForEmail);
                            _telegramService?.SendNewBookingAlertAsync(booking, orgForEmail);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to send booking notification for booking {BookingReference}", booking.BookingReference);
                    }

                    return BookingResult.Ok(booking);
                }
                catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex, "IX_Bookings_BookingReference"))
                {
                    // Reference collision, try again with next sequence
                    _context.Entry(booking).State = EntityState.Detached;
                }
                catch (DbUpdateException ex) when (
                    IsUniqueConstraintViolation(ex, "IX_BookingTimeSlot_CourtId_BookingDate_TimeSlotId_Active")
                    || IsExclusionConstraintViolation(ex, "EX_Bookings_NoOverlap"))
                {
                    // Slot already booked - double-booking protection triggered.
                    // Detach the pending booking so the context stays usable when the
                    // caller owns the transaction; otherwise roll our own back.
                    if (transaction is not null)
                    {
                        // Clear tracked changes caused by the failed attempt before rolling back.
                        foreach (var entry in _context.ChangeTracker.Entries().ToList())
                        {
                            entry.State = EntityState.Detached;
                        }

                        await transaction.RollbackAsync();
                    }

                    return BookingResult.Fail("One or more selected TimeSlots are no longer available. Please choose another time.");
                }
            }

            if (transaction is not null)
            {
                await transaction.RollbackAsync();
            }

            return BookingResult.Fail("Unable to create booking after multiple attempts. Please try again.");
        }
        catch (Exception)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync();
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

        public async Task<List<SlotAvailability>> GetAvailableSlotsAsync(int courtId, DateOnly bookingDate)
    {
        // Get all active TimeSlots
        var timeSlots = await _context.TimeSlots
            .Where(ts => ts.Status == TimeSlotStatus.Active)
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

        return BuildAvailability(timeSlots, bookedSlotIds, maintenanceSlots);
    }

    public async Task<Dictionary<int, List<SlotAvailability>>> GetAvailabilityForAllCourtsAsync(
        IEnumerable<int> courtIds,
        DateOnly bookingDate)
    {
        var courtIdList = courtIds.Distinct().ToList();

        var timeSlots = await _context.TimeSlots
            .Where(ts => ts.Status == TimeSlotStatus.Active)
            .OrderBy(ts => ts.StartTime)
            .ToListAsync();

        // Booked slots for all requested courts in a single query.
        var bookedByCourt = (await _context.BookingTimeSlots
                .Where(bts => courtIdList.Contains(bts.CourtId)
                    && bts.BookingDate == bookingDate
                    && bts.IsActive)
                .Select(bts => new { bts.CourtId, bts.TimeSlotId })
                .ToListAsync())
            .GroupBy(x => x.CourtId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.TimeSlotId).ToList());

        // Maintenance slots for all requested courts in a single query.
        var maintenanceByCourt = (await _context.CourtTimeSlots
                .Where(cts => courtIdList.Contains(cts.CourtId)
                    && cts.AvailabilityStatus == CourtTimeSlotStatus.Maintenance)
                .Select(cts => new { cts.CourtId, cts.TimeSlotId })
                .ToListAsync())
            .GroupBy(x => x.CourtId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.TimeSlotId).ToList());

        var result = new Dictionary<int, List<SlotAvailability>>();
        foreach (var courtId in courtIdList)
        {
            bookedByCourt.TryGetValue(courtId, out var booked);
            maintenanceByCourt.TryGetValue(courtId, out var maintenance);

            result[courtId] = BuildAvailability(timeSlots, booked ?? new List<int>(), maintenance ?? new List<int>());
        }

        return result;
    }

    private static List<SlotAvailability> BuildAvailability(
        List<TimeSlot> timeSlots,
        ICollection<int> bookedSlotIds,
        ICollection<int> maintenanceSlotIds)
    {
        return timeSlots
            .Select(slot =>
            {
                var isMaintenance = maintenanceSlotIds.Contains(slot.Id);
                return new SlotAvailability
                {
                    TimeSlotId = slot.Id,
                    StartTime = slot.StartTime,
                    EndTime = slot.EndTime,
                    IsMaintenance = isMaintenance,
                    IsAvailable = !bookedSlotIds.Contains(slot.Id) && !isMaintenance
                };
            })
            .ToList();
    }

        public async Task<BookingResult> CancelBookingAsync(int bookingId)
    {
        var booking = await _context.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId);
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

        public async Task<List<Booking>> GetBookingsForAdminAsync(BookingAdminFilter filter)
    {
        var query = _context.Bookings
            .Include(b => b.Court)
            .Include(b => b.TimeSlots)
                .ThenInclude(bts => bts.TimeSlot)
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
            .Include(b => b.TimeSlots)
                .ThenInclude(bts => bts.TimeSlot)
            .FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<int> AutoCompleteExpiredBookingsAsync()
    {
        // Only Confirmed bookings transition automatically; Pending bookings still
        // require an admin decision and Cancelled/Completed are terminal.
        var confirmed = await _context.Bookings
            .Where(b => b.BookingStatus == BookingStatus.Confirmed)
            .ToListAsync();

        if (confirmed.Count == 0)
        {
            return 0;
        }

        var now = AppClock.NowLocal;
        var completed = 0;

        foreach (var booking in confirmed)
        {
            // Determine the true end instant, accounting for overnight/end-of-day ranges.
            var endLocal = AppClock.ToEndLocalDateTime(booking.BookingDate, booking.StartTime, booking.EndTime);

            if (endLocal <= now)
            {
                booking.BookingStatus = BookingStatus.Completed;
                booking.UpdatedAt = DateTime.UtcNow;
                completed++;
            }
        }

        if (completed > 0)
        {
            await _context.SaveChangesAsync();
        }

        return completed;
    }

        public async Task<BookingResult> UpdateBookingStatusAsync(int id, BookingStatus newStatus)
    {
        var booking = await _context.Bookings.FirstOrDefaultAsync(b => b.Id == id);
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

        // Phase 26: notify customer and org when booking is cancelled.
        if (newStatus == BookingStatus.Cancelled)
        {
            try
            {
                var orgForEmail = await _context.Organizations
                    .FirstOrDefaultAsync(o => o.Id == booking.OrganizationId);
                if (orgForEmail is not null)
                {
                    booking.Court ??= await _context.Courts.FindAsync(booking.CourtId);
                    _emailService?.SendBookingCancelledToCustomerAsync(booking, orgForEmail);
                    _emailService?.SendBookingCancelledToOrgAsync(booking, orgForEmail);
                    _smsService?.SendBookingCancelledToCustomerAsync(booking, orgForEmail);
                    _telegramService?.SendBookingCancelledAlertAsync(booking, orgForEmail);
                }
            }
            catch (Exception ex) { _ = ex; }
        }

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

    public static List<TimeSlot> OrderSlotsChronologically(IEnumerable<TimeSlot> slots)
    {
        var list = slots.ToList();
        if (list.Count <= 1)
        {
            return list;
        }

        var sortedByStart = list.OrderBy(s => s.StartTime).ToList();

        // Check if selection crosses midnight:
        // Has a slot ending at midnight (TimeSpan.Zero or 24h) AND a slot starting at midnight (00:00)
        bool hasSlotEndingAtMidnight = sortedByStart.Any(s => s.EndTime == TimeSpan.Zero || s.EndTime == TimeSpan.FromHours(24));
        bool hasSlotStartingAtMidnight = sortedByStart.Any(s => s.StartTime == TimeSpan.Zero);

        if (hasSlotEndingAtMidnight && hasSlotStartingAtMidnight)
        {
            // Find the split point where the jump occurs between morning slots and evening slots
            int splitIndex = -1;
            for (int i = 0; i < sortedByStart.Count - 1; i++)
            {
                var prevEnd = sortedByStart[i].EndTime == TimeSpan.Zero ? TimeSpan.FromHours(24) : sortedByStart[i].EndTime;
                if (sortedByStart[i + 1].StartTime > prevEnd)
                {
                    splitIndex = i + 1;
                    break;
                }
            }

            if (splitIndex > 0)
            {
                var day2 = sortedByStart.Take(splitIndex).ToList();
                var day1 = sortedByStart.Skip(splitIndex).ToList();
                var chronological = new List<TimeSlot>(day1);
                chronological.AddRange(day2);
                return chronological;
            }
        }

        return sortedByStart;
    }

    public async Task<(bool IsValid, string? ErrorMessage)> ValidateContinuousSlotsAsync(List<int> timeSlotIds)
    {
        if (timeSlotIds == null || timeSlotIds.Count == 0)
        {
            return (false, "At least one TimeSlot must be selected.");
        }

        // Reject duplicates up front - they would otherwise create duplicate BookingTimeSlot rows.
        if (timeSlotIds.Distinct().Count() != timeSlotIds.Count)
        {
            return (false, "Duplicate TimeSlots were selected.");
        }

        // Get all selected TimeSlots
        var rawSlots = await _context.TimeSlots
            .Where(ts => timeSlotIds.Contains(ts.Id))
            .ToListAsync();

        if (rawSlots.Count != timeSlotIds.Count)
        {
            return (false, "One or more selected TimeSlots do not exist.");
        }

        // Only active slots are bookable.
        if (rawSlots.Any(s => s.Status != TimeSlotStatus.Active))
        {
            return (false, "One or more selected TimeSlots are inactive.");
        }

        if (rawSlots.Count == 1)
        {
            return (true, null);
        }

        // Order slots chronologically (respecting overnight wrap across midnight)
        var slots = OrderSlotsChronologically(rawSlots);

        // Verify slots are continuous (no gaps).
        for (int i = 1; i < slots.Count; i++)
        {
            var previousEnd = slots[i - 1].EndTime;
            if (previousEnd == TimeSpan.Zero)
            {
                previousEnd = TimeSpan.FromHours(24);
            }

            // Normal consecutive slot
            if (slots[i].StartTime == previousEnd)
            {
                continue;
            }

            // Cross-midnight boundary transition: previous slot ended at midnight (24:00) and current slot starts at midnight (00:00)
            if (previousEnd == TimeSpan.FromHours(24) && slots[i].StartTime == TimeSpan.Zero)
            {
                continue;
            }

            return (false, "Selected TimeSlots must be continuous with no gaps.");
        }

        return (true, null);
    }

            private async Task<PriceCalculationResult> ValidateAndGetPriceAsync(int? courtId, DateOnly bookingDate, TimeSpan startTime, TimeSpan endTime)
    {
        if (bookingDate < AppClock.TodayLocal)
        {
            return PriceCalculationResult.Fail("Booking date cannot be in the past.");
        }

                // Resolve the range to absolute hour offsets. An end time at or before the
        // start (e.g. the 23:00-00:00 slot) is treated as crossing midnight, and an
        // end-of-day sentinel (23:59/00:00) is snapped to exactly 24:00 so the final
        // hour of the day is covered.
        var (startHours, endHours) = AppClock.ToAbsoluteRangeNormalized(startTime, endTime);

        if (endHours <= startHours)
        {
            return PriceCalculationResult.Fail("End time must be after start time.");
        }

        if (endHours > 24)
        {
            // Overnight range crossing midnight:
            // Day 1 portion (startTime to midnight on bookingDate)
            var day1Result = await CalculatePriceAsync(bookingDate, startTime, TimeSpan.Zero, courtId);
            if (!day1Result.Success)
            {
                return day1Result;
            }

            // Day 2 portion (midnight to endTime on next calendar day)
            var day2Result = await CalculatePriceAsync(bookingDate.AddDays(1), TimeSpan.Zero, endTime, courtId);
            if (!day2Result.Success)
            {
                return day2Result;
            }

            return PriceCalculationResult.Ok(decimal.Round(day1Result.Price + day2Result.Price, 2));
        }

                if (courtId.HasValue)
        {
            var court = await _context.Courts.FirstOrDefaultAsync(c => c.Id == courtId.Value);
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

                // Bands may also cross midnight (e.g. 18:00-02:00). Resolve each to absolute hours,
        // snapping an end-of-day sentinel (23:59/00:00) to exactly 24:00 so the last hour
        // of the day is priced.
        var normalizedBands = pricingBands
            .Select(p =>
            {
                var (bandStart, bandEnd) = AppClock.ToAbsoluteRangeNormalized(p.StartTime, p.EndTime);
                return (Start: bandStart, End: bandEnd, p.Price);
            })
            .OrderBy(b => b.Start)
            .ToList();

        var cursor = startHours;
        decimal totalPrice = 0m;

        while (cursor < endHours)
        {
            var band = normalizedBands.FirstOrDefault(b => b.Start <= cursor && b.End > cursor);
            if (band == default)
            {
                return PriceCalculationResult.Fail("Pricing is not configured for the selected date and time.");
            }

            var segmentEnd = normalizedBands
                .Where(b => b.Start > cursor)
                .Select(b => b.Start)
                .DefaultIfEmpty(endHours)
                .Min();

            if (segmentEnd > band.End)
            {
                segmentEnd = band.End;
            }

            if (segmentEnd > endHours)
            {
                segmentEnd = endHours;
            }

            if (segmentEnd <= cursor)
            {
                return PriceCalculationResult.Fail("Pricing is not configured for the selected date and time.");
            }

            totalPrice += (decimal)(segmentEnd - cursor) * band.Price;
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
