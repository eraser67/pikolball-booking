namespace PickleBallBooking.Services;

public interface IBookingService
{
    Task<bool> IsAvailableAsync(int courtId, int timeSlotId, DateOnly bookingDate);

    Task<PriceCalculationResult> CalculatePriceAsync(int courtId, int timeSlotId, DateOnly bookingDate);

    Task<Models.Booking?> LookupBookingAsync(string bookingReference, string contactInfo);

    Task<BookingResult> CreateBookingAsync(
        int courtId,
        int timeSlotId,
        DateOnly bookingDate,
        string customerName,
        string customerPhone,
        string customerEmail);

    Task<List<Models.Booking>> GetBookingsForAdminAsync(BookingAdminFilter filter);

    Task<Models.Booking?> GetBookingByIdAsync(int id);

    Task<BookingResult> UpdateBookingStatusAsync(int id, Models.BookingStatus newStatus);
}
