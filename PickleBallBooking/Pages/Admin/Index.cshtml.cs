using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin;

[Authorize(Policy = TenantAdminAuthorization.TenantAdminPolicy)]
public class IndexModel : PageModel
{
        private readonly ApplicationDbContext _context;
    private readonly IBookingService _bookingService;
    private readonly IOrganizationService _organizationService;
    private readonly ISubscriptionService _subscriptionService;

    public IndexModel(
        ApplicationDbContext context,
        IBookingService bookingService,
        IOrganizationService organizationService,
        ISubscriptionService subscriptionService)
    {
        _context = context;
        _bookingService = bookingService;
        _organizationService = organizationService;
        _subscriptionService = subscriptionService;
    }

    public Models.Organization? Organization { get; set; }

    // Tenant-level stats
    public int TodaysBookingsCount { get; set; }

    public int PendingBookingsCount { get; set; }

    public int ConfirmedBookingsCount { get; set; }

    public int CompletedBookingsCount { get; set; }

    public int ActiveCourtsCount { get; set; }

    // Platform-level stats (only loaded for PlatformAdmin)
    public bool IsPlatformAdmin { get; set; }
    public int TotalOrgsCount { get; set; }
    public int ActiveOrgsCount { get; set; }
    public int InactiveOrgsCount { get; set; }
    public int ActiveSubscriptionsCount { get; set; }
    public int TrialSubscriptionsCount { get; set; }
    public int ExpiredOrSuspendedCount { get; set; }

        public async Task OnGetAsync()
    {
                // Keep the dashboard counts accurate by completing expired confirmed bookings first.
        await _bookingService.AutoCompleteExpiredBookingsAsync();

        Organization = await _organizationService.GetCurrentAsync();

        var today = AppClock.TodayLocal;

        TodaysBookingsCount = await _context.Bookings.CountAsync(b => b.BookingDate == today);
        PendingBookingsCount = await _context.Bookings.CountAsync(b => b.BookingStatus == BookingStatus.Pending);
        ConfirmedBookingsCount = await _context.Bookings.CountAsync(b => b.BookingStatus == BookingStatus.Confirmed);
        CompletedBookingsCount = await _context.Bookings.CountAsync(b => b.BookingStatus == BookingStatus.Completed);
        ActiveCourtsCount = await _context.Courts.CountAsync(c => c.Status == CourtStatus.Active);

        // Platform stats — only loaded when the logged-in user is a PlatformAdmin.
        IsPlatformAdmin = User.IsInRole(PlatformRoles.PlatformAdmin);
        if (IsPlatformAdmin)
        {
            var allOrgs = await _organizationService.GetAllAsync();
            TotalOrgsCount    = allOrgs.Count;
            ActiveOrgsCount   = allOrgs.Count(o => o.Status == OrganizationStatus.Active);
            InactiveOrgsCount = allOrgs.Count(o => o.Status == OrganizationStatus.Inactive);

            var allSubs = await _subscriptionService.GetAllSubscriptionsAsync();
            ActiveSubscriptionsCount  = allSubs.Count(s => s.Status == SubscriptionStatus.Active);
            TrialSubscriptionsCount   = allSubs.Count(s => s.Status == SubscriptionStatus.Trial);
            ExpiredOrSuspendedCount   = allSubs.Count(s =>
                s.Status is SubscriptionStatus.Expired or SubscriptionStatus.Suspended or SubscriptionStatus.Cancelled);
        }
    }
}
