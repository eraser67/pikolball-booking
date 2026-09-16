namespace PickleBallBooking.Services;

public interface IBookingService
{
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
}
