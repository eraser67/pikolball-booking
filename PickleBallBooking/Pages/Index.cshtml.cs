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
        private readonly ICourtImageStorage _imageStorage;
        private readonly IOrganizationService _organizationService;
        private readonly ActivityService _activityService;

        public IndexModel(
            ICourtService courtService,
            IBookingService bookingService,
            ITimeSlotService timeSlotService,
            ICourtImageStorage imageStorage,
            IOrganizationService organizationService,
            ActivityService activityService)
        {
            _courtService        = courtService;
            _bookingService      = bookingService;
            _timeSlotService     = timeSlotService;
            _imageStorage        = imageStorage;
            _organizationService = organizationService;
            _activityService     = activityService;
        }

        /// <summary>Active courts loaded dynamically from the database.</summary>
        public List<CourtCardViewModel> Courts { get; set; } = new();

        /// <summary>Date the preview availability refers to (today).</summary>
        public DateOnly PreviewDate { get; set; }

        // ── Location (from Organization settings) ──
        public string OrgName { get; set; } = "Our Pickleball Club";
        public string? OrgAddress { get; set; }
        public double OrgLatitude  { get; set; } = 10.671029;
        public double OrgLongitude { get; set; } = 124.020279;
        public bool HasMapCoordinates => OrgAddress is not null || (OrgLatitude != 0 && OrgLongitude != 0);

        // ── Tenant Branding ──
        public string? HeroImageUrl { get; set; }
        public string? Tagline { get; set; }
        public string? AboutText { get; set; }
        public bool ShowActivitiesOnHome { get; set; }
        public List<Activity> UpcomingActivities { get; set; } = new();

        // ── Section Visibility (defaults match model defaults = all visible) ──
        public bool ShowHowItWorksSection { get; set; } = true;
        public bool ShowWhyUsSection      { get; set; } = true;
        public bool ShowFaqSection        { get; set; } = true;
        public bool ShowLocationSection   { get; set; } = true;

        // ── Amenities ──
        public bool ShowAmenitiesSection  { get; set; } = false;
        public IReadOnlyList<VenueAmenity> Amenities { get; set; } = Array.Empty<VenueAmenity>();

        // ── Opening Hours ──
        public string? OpeningHours { get; set; }

        // ── Announcement Banner ──
        public string? AnnouncementText       { get; set; }
        public bool    ShowAnnouncementBanner  { get; set; }

        public async Task OnGetAsync()
        {
            PreviewDate = AppClock.TodayLocal;

            // Load organization location for the Find Us section.
            var org = await _organizationService.GetCurrentAsync();
            if (org is not null)
            {
                OrgName      = org.Name;
                OrgAddress   = org.Address;
                OrgLatitude  = org.Latitude  ?? 10.671029;
                OrgLongitude = org.Longitude ?? 124.020279;

                // Use custom hero image if the org has uploaded one.
                HeroImageUrl = _imageStorage.GetPublicUrl(org.HeroImagePath);

                // Branding
                Tagline              = org.Tagline;
                AboutText            = org.AboutText;
                ShowActivitiesOnHome = org.ShowActivitiesOnHome;

                // Section toggles
                ShowHowItWorksSection = org.ShowHowItWorksSection;
                ShowWhyUsSection      = org.ShowWhyUsSection;
                ShowFaqSection        = org.ShowFaqSection;
                ShowLocationSection   = org.ShowLocationSection;

                // Amenities
                ShowAmenitiesSection = org.ShowAmenitiesSection;
                Amenities            = VenueAmenities.Parse(org.AmenitiesKeys);

                // Opening hours + announcement
                OpeningHours           = org.OpeningHours;
                AnnouncementText       = org.AnnouncementText;
                ShowAnnouncementBanner = org.ShowAnnouncementBanner;

                // Load upcoming activities if the tenant has opted in.
                if (org.ShowActivitiesOnHome)
                {
                    UpcomingActivities = (await _activityService.GetPublishedAsync())
                        .Where(a => a.Date >= AppClock.TodayLocal)
                        .OrderBy(a => a.Date).ThenBy(a => a.StartTime)
                        .Take(3)
                        .ToList();
                }
            }

            var courts = await _courtService.GetAllAsync();
            var timeSlots = await _timeSlotService.GetActiveAsync();

            Courts = new List<CourtCardViewModel>();

            if (courts.Count == 0)
            {
                return;
            }

            var activeCourts = courts.Where(c => c.Status == CourtStatus.Active).ToList();

            // Single round-trip availability lookup for active courts
            var availabilityByCourt = (timeSlots.Count == 0 || activeCourts.Count == 0)
                ? null
                : await _bookingService.GetAvailabilityForAllCourtsAsync(activeCourts.Select(c => c.Id), PreviewDate);

            var index = 0;
            foreach (var court in courts)
            {
                var totalSlots = timeSlots.Count;
                var availableSlots = 0;
                var isCourtActive = court.Status == CourtStatus.Active;

                if (isCourtActive && availabilityByCourt is not null
                    && availabilityByCourt.TryGetValue(court.Id, out var slots))
                {
                    availableSlots = slots.Count(s => s.IsAvailable);
                }

                // Phase 24: use the uploaded Supabase image when available;
                // fall back to the static placeholder artwork otherwise.
                var uploadedUrl = _imageStorage.GetPublicUrl(court.ImagePath);

                Courts.Add(new CourtCardViewModel
                {
                    Court = court,
                    ImageUrl = uploadedUrl ?? CourtImageFor(index),
                    TotalSlots = totalSlots,
                    AvailableSlots = availableSlots,
                    HasAvailabilityData = isCourtActive && availabilityByCourt is not null
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
                "/images/court1.png",
                "/images/court2.png",
                "/images/court3.png",
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
