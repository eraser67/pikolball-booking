using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public class BookingResult
{
    public bool Success { get; init; }

    public string? ErrorMessage { get; init; }

    public Booking? Booking { get; init; }

    public static BookingResult Fail(string errorMessage) => new() { Success = false, ErrorMessage = errorMessage };

    public static BookingResult Ok(Booking booking) => new() { Success = true, Booking = booking };
}
