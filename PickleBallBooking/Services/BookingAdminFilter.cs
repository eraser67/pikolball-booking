using PickleBallBooking.Models;

namespace PickleBallBooking.Services;

public class BookingAdminFilter
{
    public DateOnly? BookingDate { get; set; }

    public int? CourtId { get; set; }

    public BookingStatus? Status { get; set; }

    public string? CustomerSearch { get; set; }
}
