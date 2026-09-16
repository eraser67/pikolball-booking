# Pickleball Booking System

A simple, maintainable, responsive web-based Pickleball Court Booking System.

Built with:
- **C#** and **ASP.NET Core** (.NET 10)
- **Razor Pages** for UI
- **Entity Framework Core** for data
- **PostgreSQL** / **Supabase** for database
- **Bootstrap 5** for responsive design
- **ASP.NET Core Identity** for authentication

**Status:** Phases 1–15 Complete | Documentation Updated for Phases 16–18

---

## Features

### Customer Experience

- Modern calendar-style booking interface
- Browse available courts
- Select booking date
- View fixed hourly TimeSlots (24 slots per day: 12 AM–11 PM)
- See TimeSlot availability (Available, Booked, Maintenance/Unavailable)
- Select continuous range of hourly TimeSlots for one reservation
- View calculated duration and price
- Book without creating an account
- Enter name, phone, and email at booking
- Receive unique booking reference
- Look up booking status using reference + phone/email

### Admin Experience

- Secure login via ASP.NET Core Identity
- Dashboard with booking summary
- Manage courts (add, edit, activate, deactivate)
- Manage TimeSlots (activate/deactivate)
- Configure pricing (weekday/weekend, by time period, overnight ranges)
- View all bookings
- Search and filter bookings (by date, court, status, customer)
- Confirm, cancel, and complete bookings
- View schedule/calendar

### System Features

- **Fixed Hourly TimeSlots** — 24 standard 1-hour bookable units per day
- **Continuous Multi-TimeSlot Bookings** — One reservation for multiple consecutive hours
- **Per-TimeSlot Availability** — Clear booking status for each hour
- **Configurable Pricing** — Different rates by weekday/weekend and time period
- **Overnight Pricing** — Support for ranges crossing midnight (e.g., 6 PM–2 AM)
- **Double Booking Protection** — Prevents concurrent overlapping reservations
- **Back-to-Back Bookings** — Allowed (one booking ends where another starts)
- **Cancellation Support** — Cancelled bookings immediately release TimeSlots
- **Court-Specific Maintenance** — Mark courts/hours as unavailable
- **Server-Side Authority** — All pricing and availability validated server-side
- **Database-Level Protection** — Unique constraints prevent duplicate bookings
- **Automated Tests** — Comprehensive test coverage

---

## Fixed Hourly TimeSlots

The system uses **24 standard fixed hourly TimeSlots** as the atomic unit of booking:

```
12:00 AM – 1:00 AM through 11:00 PM – 12:00 AM
```

### Continuous Bookings

Customers select **multiple consecutive hourly TimeSlots** to create one reservation.

Example:
- Select TimeSlots: 6:00 PM, 7:00 PM, 8:00 PM, 9:00 PM (4 consecutive hours)
- Creates ONE Booking record with StartTime: 6:00 PM, EndTime: 10:00 PM, Duration: 4 hours
- **NOT** four separate one-hour bookings

---

## Key Design Features

### Fixed Hourly TimeSlots (vs. Arbitrary Time Ranges)
- 24 predefined bookable hours per day
- Clear per-TimeSlot availability status
- Simpler pricing: ₱X per hour × selected hours
- Easier conflict detection

### One Booking Per Reservation
- One database record = one customer reservation
- Multi-hour booking created in one interaction
- Simpler to manage and report

### Server-Side Authority
- All pricing calculated server-side
- All availability validated server-side
- Customer cannot manipulate price or booking status
- Atomic database transactions prevent race conditions

### Back-to-Back Bookings Allowed
- One booking can end where another starts
- No "dead time" between bookings
- Example: 18:00–20:00 and 20:00–22:00 are both allowed

### Cancellation Support
- Cancelled bookings immediately release TimeSlots
- Released TimeSlots become available for future bookings
- Bookings are deactivated, not deleted (preserves history)

---

## Quick Start

### Prerequisites
- Visual Studio Community 2026
- .NET 10 SDK
- PostgreSQL or Supabase (free tier)
- Git

### Setup

1. Clone repository
   ```bash
   git clone https://github.com/eraser67/pikolball-booking.git
   cd pikolball-booking
   ```

2. Configure Supabase connection string via User Secrets
   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "your_connection_string"
   ```

3. Run migrations
   ```bash
   dotnet ef database update
   ```

4. Run application
   ```bash
   dotnet run
   ```

5. Access
   - Home: http://localhost:5000
   - Admin: http://localhost:5000/Admin

---

## Architecture

Simple monolithic Razor Pages application:

```
Customer/Admin
    ↓
ASP.NET Core Razor Pages
    ↓
Services (Booking, Court, TimeSlot, Pricing, Availability)
    ↓
Entity Framework Core
    ↓
PostgreSQL / Supabase
```

---

## Fixed Hourly TimeSlots Architecture

The system uses **24 standard fixed hourly TimeSlots** as the atomic unit:

```
00:00–01:00 (12:00 AM–1:00 AM)
01:00–02:00 (1:00 AM–2:00 AM)
...
22:00–23:00 (10:00 PM–11:00 PM)
23:00–00:00 (11:00 PM–12:00 AM)
```

Each TimeSlot is **individually** Active or Inactive.

Customers select **consecutive TimeSlots** to create a single booking.

---

## Testing

Run automated tests:
```bash
dotnet test
```

Tests cover:
- Multi-hour continuous booking logic
- TimeSlot availability determination
- Overlap detection and prevention
- Pricing calculations (per-TimeSlot, weekday/weekend, overnight ranges)
- Server-side price authority
- Concurrent booking protection
- Cancellation and TimeSlot release
- Existing functionality preservation

---

## Development Status

### Completed (Phases 1–15)
- [x] Environment setup
- [x] Database models and migrations
- [x] Court management
- [x] TimeSlot management
- [x] Pricing management
- [x] Booking pages
- [x] Availability display
- [x] Double booking protection
- [x] Booking lookup
- [x] Admin authentication
- [x] Admin dashboard
- [x] Booking management
- [x] Schedule view
- [x] Security and error handling

### In Progress (Documentation)
- [x] Update PROJECT_REQUIREMENTS.md for fixed hourly TimeSlot design
- [x] Update DEVELOPMENT_PLAN.md with new phases
- [x] Update TODO.md
- [x] Update README.md

### Upcoming (Pending Approval)
- **Phase 16:** Booking Model Redesign to fixed hourly TimeSlots
- **Phase 17:** Fixed-TimeSlot Availability UI
- **Phase 18:** UI/UX Refinement (Bootstrap 5)

---

## Important Notes

- **No Payment System** — Version 1 has no online payment functionality
- **No Customer Registration** — Customers book anonymously
- **Fixed 1-Hour TimeSlots** — Not arbitrary time ranges
- **One Booking Per Reservation** — Not per-hour records
- **Server-Side Authority** — Frontend is informational only

---

## Cost

**Target:** ₱0 (free/open-source only)

Uses:
- Free Visual Studio Community
- Free GitHub
- Free .NET
- Free Bootstrap
- Free Supabase (PostgreSQL free tier)

No paid services required.

---

## License

MIT License

---

**Last Updated:** 2026-01-XX  
**Documentation Version:** Updated for Phases 16–18 redesign