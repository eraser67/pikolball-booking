using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PickleBallBooking.Data;
using PickleBallBooking.Models;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Customer;

/// <summary>
/// Phase 31: customer / player dashboard.
///
/// Displays the authenticated player's account summary:
///   - Full name and email (from Identity claims / user)
///   - Recent bookings linked to their email address
///
/// Authorization: requires the Customer role (enforced by AuthorizeFolder in
/// Program.cs via the CustomerOnly policy).
/// </summary>
public class DashboardModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ApplicationDbContext _context;

    public DashboardModel(
        UserManager<IdentityUser> userManager,
        ApplicationDbContext context)
    {
        _userManager = userManager;
        _context = context;
    }

    public string FullName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string Mobile { get; private set; } = string.Empty;

    /// <summary>
    /// The 5 most recent bookings matched by the customer's email address.
    /// Anonymous bookings made before account creation are included.
    /// </summary>
    public IReadOnlyList<BookingSummary> RecentBookings { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToPage("/Account/Login");
        }

        Email  = user.Email ?? string.Empty;
        Mobile = user.PhoneNumber ?? string.Empty;

        // Full name is stored as a claim in Phase 31.
        // Phase 32 (PlayerProfile) moves this to a proper DB entity.
        FullName = User.FindFirstValue("fullName") ?? Email;

        // Fetch the 5 most recent bookings linked to this customer's email.
        // IgnoreQueryFilters bypasses the tenant (OrganizationId) global query filter
        // so that bookings from all tenant venues the customer has used are returned.
        // Phase 32 introduces a proper cross-tenant participation model.
        RecentBookings = await _context.Bookings
            .IgnoreQueryFilters()
            .Include(b => b.Court)
            .Where(b => b.CustomerEmail == Email)
            .OrderByDescending(b => b.CreatedAt)
            .Take(5)
            .Select(b => new BookingSummary
            {
                Id          = b.Id,
                CourtName   = b.Court != null ? b.Court.Name : "Court",
                Date        = b.BookingDate,
                StartTime   = b.StartTime,
                EndTime     = b.EndTime,
                Status      = b.BookingStatus,
                Price       = b.Price,
                Reference   = b.BookingReference,
                CreatedAt   = b.CreatedAt
            })
            .ToListAsync();

        return Page();
    }

    public record BookingSummary
    {
        public int Id { get; init; }
        public string CourtName { get; init; } = string.Empty;
        public DateOnly Date { get; init; }
        public TimeSpan StartTime { get; init; }
        public TimeSpan EndTime { get; init; }
        public BookingStatus Status { get; init; }
        public decimal Price { get; init; }
        public string Reference { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
    }
}
