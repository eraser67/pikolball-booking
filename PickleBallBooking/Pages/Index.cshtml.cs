using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages
{
    /// <summary>
    /// Home page. Purely presents existing courts and a lightweight, clearly-labelled
    /// availability PREVIEW for today. It reuses the existing services and does not
    /// duplicate any booking / availability logic.
    /// </summary>
    public class IndexModel : PageModel
    {
        private readonly ICourtService _courtService;
        private readonly IBookingService _bookingService;
        private readonly ITimeSlotService _timeSlotService;

        public IndexModel(ICourtService courtService, IBookingService bookingService, ITimeSlotService timeSlotService)
        {
            _courtService = courtService;
            _bookingService = bookingService;
            _timeSlotService = timeSlotService;
        }

        /// <summary>Active courts loaded dynamically from the database.</summary>
        public List<CourtCardViewModel> Courts { get; set; } = new();

        /// <summary>Date the preview availability refers to (today).</summary>
        public DateOnly PreviewDate { get; set; }

        public async Task OnGetAsync()
        {
            PreviewDate = AppClock.TodayLocal;

            var courts = await _courtService.GetActiveAsync();
            var timeSlots = await _timeSlotService.GetActiveAsync();

            Courts = new List<CourtCardViewModel>();

            if (courts.Count == 0)
            {
                return;
            }

            // Single round-trip availability lookup for every active court (reuses existing service).
            var availabilityByCourt = timeSlots.Count == 0
                ? null
                : await _bookingService.GetAvailabilityForAllCourtsAsync(courts.Select(c => c.Id), PreviewDate);

            var index = 0;
            foreach (var court in courts)
            {
                var totalSlots = timeSlots.Count;
                var availableSlots = 0;

                if (availabilityByCourt is not null
                    && availabilityByCourt.TryGetValue(court.Id, out var slots))
                {
                    availableSlots = slots.Count(s => s.IsAvailable);
                }

                Courts.Add(new CourtCardViewModel
                {
                    Court = court,
                    ImageUrl = CourtImageFor(index),
                    TotalSlots = totalSlots,
                    AvailableSlots = availableSlots,
                    HasAvailabilityData = availabilityByCourt is not null
                });

                index++;
            }
        }

        /// <summary>
        /// Cycles through the available court artwork so dynamically added courts
        /// always get a reasonable image without any code change.
        /// </summary>
        private static string CourtImageFor(int index)
        {
            var images = new[]
            {
                "/images/serve.png",
                "/images/serve-alt.png",
                "/images/hero-player.png",
                "/images/pickleball.png"
            };

            return images[index % images.Length];
        }

        public class CourtCardViewModel
        {
            public Court Court { get; set; } = null!;
            public string ImageUrl { get; set; } = string.Empty;
            public int TotalSlots { get; set; }
            public int AvailableSlots { get; set; }
            public bool HasAvailabilityData { get; set; }

            /// <summary>True when at least one slot is still open today (preview only).</summary>
            public bool HasLiveAvailability => HasAvailabilityData && AvailableSlots > 0;

            /// <summary>True when the court is fully booked / unavailable today (preview only).</summary>
            public bool IsFullyBooked => HasAvailabilityData && AvailableSlots == 0;
        }
    }
}
