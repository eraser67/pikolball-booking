namespace PickleBallBooking.Services;

public interface IBookingService
{
    /// <summary>
    /// Determines whether a court is free for the given date and time range.
    /// Returns false if the range overlaps an existing active booking OR if any
    /// part of the range falls on a court time slot marked as maintenance.
    /// </summary>
    Task<bool> IsAvailableAsync(int courtId, DateOnly bookingDate, TimeSpan startTime, TimeSpan endTime);
    /// <summary>
    /// Calculates the price for a date and time range, validating that the court is
    /// active, the range is in the future and pricing is configured for every hour.
    /// When a court is supplied the range availability is also validated.
    /// </summary>
    Task<PriceCalculationResult> CalculatePriceAsync(DateOnly bookingDate, TimeSpan startTime, TimeSpan endTime, int? courtId = null);

    Task<Models.Booking?> LookupBookingAsync(string bookingReference, string contactInfo);

    Task<List<Models.Booking>> GetBookingsForAdminAsync(BookingAdminFilter filter);

    Task<Models.Booking?> GetBookingByIdAsync(int id);

    Task<BookingResult> UpdateBookingStatusAsync(int id, Models.BookingStatus newStatus);

    /// <summary>
    /// Automatically marks any <see cref="Models.BookingStatus.Confirmed"/> booking whose
    /// end date/time has already passed as <see cref="Models.BookingStatus.Completed"/>.
    /// Returns the number of bookings that were transitioned.
    /// </summary>
    Task<int> AutoCompleteExpiredBookingsAsync();
    /// <summary>
    /// Get available TimeSlots for a specific court on a specific date.
    /// Returns slot IDs and their booking status.
    /// </summary>
    Task<List<SlotAvailability>> GetAvailableSlotsAsync(int courtId, DateOnly bookingDate);
    /// <summary>
    /// Get availability for every active court on a specific date in a single round-trip.
    /// Keyed by court id.
    /// </summary>
    Task<Dictionary<int, List<SlotAvailability>>> GetAvailabilityForAllCourtsAsync(IEnumerable<int> courtIds, DateOnly bookingDate);

    /// <summary>
    /// Validate that selected TimeSlot IDs are consecutive (no gaps) in chronological order.
    /// </summary>
    Task<(bool IsValid, string? ErrorMessage)> ValidateContinuousSlotsAsync(List<int> timeSlotIds);

    /// <summary>
    /// Create a booking with multiple hourly TimeSlots.
    /// Supports back-to-back continuous slots with database-level double-booking protection.
    /// </summary>
    Task<BookingResult> CreateBookingWithSlotsAsync(
        int courtId,
        DateOnly bookingDate,
        List<int> timeSlotIds,
        string customerName,
        string customerPhone,
        string customerEmail);

    /// <summary>
    /// Cancel a booking and release all BookingTimeSlot records by setting IsActive = false.
    /// </summary>
    Task<BookingResult> CancelBookingAsync(int bookingId);
}

