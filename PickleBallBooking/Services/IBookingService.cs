namespace PickleBallBooking.Services;

public interface IBookingService
{
    // Legacy range-based methods (for backward compatibility)
    Task<bool> IsAvailableAsync(int courtId, DateOnly bookingDate, TimeSpan startTime, TimeSpan endTime);

    Task<PriceCalculationResult> CalculatePriceAsync(DateOnly bookingDate, TimeSpan startTime, TimeSpan endTime);

    Task<Models.Booking?> LookupBookingAsync(string bookingReference, string contactInfo);

    Task<BookingResult> CreateBookingAsync(
        int courtId,
        DateOnly bookingDate,
        TimeSpan startTime,
        TimeSpan endTime,
        string customerName,
        string customerPhone,
        string customerEmail);

    Task<List<Models.Booking>> GetBookingsForAdminAsync(BookingAdminFilter filter);

    Task<Models.Booking?> GetBookingByIdAsync(int id);

    Task<BookingResult> UpdateBookingStatusAsync(int id, Models.BookingStatus newStatus);

    // New slot-based methods for fixed-hour architecture
    /// <summary>
    /// Get available TimeSlots for a specific court on a specific date.
    /// Returns slot IDs and their booking status.
    /// </summary>
    Task<List<SlotAvailability>> GetAvailableSlotsAsync(int courtId, DateOnly bookingDate);

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
