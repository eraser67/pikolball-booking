<div align="center">
  <img src="PickleBallBooking/wwwroot/images/pickleball.png" width="72" height="72" alt="Pikolball Logo" />
  <h1>Pikolball SaaS</h1>
  <p><strong>Multi-Tenant Pickleball Court Booking, Venue Operations & Competitive Community Platform</strong></p>

  <p>
    <img src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 10" />
    <img src="https://img.shields.io/badge/C%23-14-239120?logo=csharp&logoColor=white" alt="C#" />
    <img src="https://img.shields.io/badge/PostgreSQL-Supabase-336791?logo=postgresql&logoColor=white" alt="PostgreSQL" />
    <img src="https://img.shields.io/badge/Tests-438%20Passed-success?logo=github-actions&logoColor=white" alt="438 Tests Passed" />
    <img src="https://img.shields.io/badge/Status-Production%20Live-brightgreen" alt="Production Live" />
  </p>
</div>

---

## Overview

**Pikolball SaaS** (Punit Bola) is a multi-tenant sports management and reservation system built for pickleball clubs, municipal venues, and sports facilities. Each organization operates on its own dedicated subdomain with complete data isolation, customized branding, venue-specific GCash payment gateways, and role-based access.

Beyond standard hourly court reservations, Pikolball provides an end-to-end community and competitive ecosystem: player accounts, event RSVP and waitlist automation, recurring weekly sessions, on-site check-in, dynamic court assignment, automated Round Robin mixers, match scoring, leaderboards, and career player statistics.

> [!NOTE]
> **Production Status:** Phases 1–43 are complete and live in production at [`https://punitbola.tech`](https://punitbola.tech) and [`https://demo.punitbola.tech`](https://demo.punitbola.tech). The automated test suite contains **438 passing unit and integration tests**.

---

## Architecture

Pikolball uses a database-level multi-tenant architecture with global query filtering and hostname resolution.

```
                         Internet / Player / Tenant Admin
                                        ↓
                       Nginx Reverse Proxy (*.punitbola.tech)
                         Let's Encrypt Wildcard SSL Certificate
                                        ↓
                              ASP.NET Core (.NET 10)
                                        ↓
┌───────────────────────────────────────┴───────────────────────────────────────┐
│ Middleware & Security Pipeline:                                               │
│  - TenantResolutionMiddleware  (Subdomain → Organization mapping)             │
│  - SubscriptionWallMiddleware  (Enforces active tenant subscription tiers)    │
│  - ITenantContext / Scoped Service Provider                                   │
├───────────────────────────────────────────────────────────────────────────────┤
│ Core Business Services:                                                       │
│  - BookingService, CourtService, PricingService                               │
│  - CustomerRegistrationService, PlayerProfileService, TenantPlayerService     │
│  - ActivityService, ActivityRsvpService, ActivitySeriesService                │
│  - PlayerCheckInService, CourtAssignmentService                               │
│  - RoundRobinService, MatchScoringService, StandingsService                   │
│  - PlayerStatisticsService                                                    │
│  - PaymentService (GCash Manual + Supabase Private Proof Storage)             │
│  - AppNotificationService (Multi-channel: Email, SMS Semaphore, Telegram)     │
└───────────────────────────────────────┬───────────────────────────────────────┘
                                        ↓
                             Entity Framework Core
                (Global Query Filter: e => e.OrganizationId == currentTenantId)
                                        ↓
                         PostgreSQL Database (Supabase)
```

---

## Features

### 1. Customer Experience & Court Booking
- **Branded Homepage:** Dynamic hero, court showcase with availability preview, venue information, and responsive layout.
- **24 Standardized Hourly TimeSlots:** Fixed 1-hour bookable units (00:00–01:00 through 23:00–00:00) with zero scheduling fragmentation.
- **Dynamic Calendar-Style Booking:**
  - Fast court and date switching via **AJAX** without page refresh or URL disruption (`OnGetSlotsAsync`).
  - Real-time status badges (*Available*, *Booked*, *Maintenance*).
  - Past slots disabled automatically for the current date.
- **Continuous Multi-Hour Selection:** Select consecutive hours for a single unified reservation.
- **Interactive Timeslot Gap Error Alert:** Real-time visual warning (`"Cannot select time slot with gap."` with shake animation) when non-consecutive slots are clicked.
- **Two-Phase Booking UX:**
  - Review court, duration, and server-calculated price in Philippine Peso (`₱`).
  - Step 4 ("Your Information") is strictly revealed after clicking **Calculate Price**.
  - On mobile devices, clicking Calculate Price triggers a smooth auto-scroll to Step 4 and focuses the Full Name input.
- **Anonymous & Member Reservations:** Book courts anonymously (no account required) or automatically link bookings to a registered Player profile.
- **Collision-Proof Booking References:** Multi-tenant sequential reference generation (e.g., `PB-20260921-0001`).
- **Self-Service Booking Lookup:** Customers can search reservations using their booking reference and email/phone without logging in.

### 2. Manual GCash Payment Workflow
- **Organization-Specific Payment Details:** Display custom GCash account name, number, QR code, and payment instructions configured by each venue.
- **Proof of Payment Upload:** Customers submit their GCash transaction reference number and upload receipt screenshot proof.
- **Supabase Private Storage:** Receipts are stored in a private object storage bucket, inaccessible to the public.
- **Admin Verification Portal:** Short-lived signed URLs (5-minute expiry) allowing venue admins to inspect payment receipts, approve, or reject with explanatory notes.

### 3. Community Foundation & Customer Identity (Phases 31–36)
- **Player Accounts & Authentication:** Platform-level player identity (`Player` role) separate from venue administrative staff.
- **Player Profiles:** Customizable player records including skill level, playing hand, paddle preference, DUPR rating placeholder, bio, and emergency contact details.
- **Activities & Social Events:** Open Play sessions, clinics, drills, and structured formats with per-player pricing or free admission.
- **RSVP & Automated Waitlist:** Real-time capacity tracking, automated waitlisting when capacity is reached, and instant promotion when registered players cancel.
- **Recurring Activity Series:** Weekly recurring rule engine generating multi-week session schedules.
- **Multi-Channel Notifications:** Event updates and RSVP confirmations dispatched via Transactional Email (`MailKit`), SMS (`Semaphore`), and Telegram bot.

### 4. Venue Operations & Attendance (Phases 38–39)
- **Player Check-In & Attendance:** 1-click and QR-based on-site check-in (`CheckedIn`, `NoShow`, `Late`).
- **Live Attendance Dashboard:** Real-time venue view of present players, confirmed RSVPs, and walk-in arrivals.
- **Court Assignment Engine:** Automated and manual allocation of checked-in players to physical courts with live status management (`Warmup`, `InPlay`, `Completed`).

### 5. Competitive Play, Round Robin & Leaderboards (Phases 40–43)
- **Round Robin Engine:** Automated fair-schedule generation for social mixers and tournaments, rotating partner pairings, court allocations, and bye handling.
- **Match Scoring:** Standard 11-point and 15-point game support with strict win-by-2 score verification.
- **Live Standings & Leaderboards:** Real-time tracking of matches played, wins, losses, total points scored/conceded, and point differential.
- **Player Career Statistics:** Individual match history, win rates, game counts, and performance metrics across venues.
- **Tenant-Managed Walk-In Players:** Venue admin capability to create and manage guest players directly on-site for immediate tournament inclusion.

### 6. Tenant Admin Experience (Venue Management)
- **Dedicated Venue Admin Portal:** Scoped to the authenticated organization admin.
- **Interactive Dashboard:** Summary cards for daily bookings, pending verifications, monthly revenue in ₱, and active courts.
- **Court Management:** Add, edit, upload court images (via Supabase Storage), activate, and deactivate courts. Auto-initializes 24 hourly `CourtTimeSlot` records for immediate availability.
- **TimeSlot & Maintenance Management:** Configure bookable operating hours and flag individual courts/hours for maintenance.
- **Flexible Pricing Engine:**
  - Configurable rates for weekdays vs. weekends.
  - Multiple time period tiers (e.g., peak evening vs. off-peak morning).
  - Overnight pricing support crossing midnight (e.g., 6:00 PM – 2:00 AM).
- **Booking Management:** Search, filter by date/court/status, confirm pending reservations, cancel bookings, and view complete history.
- **Schedule Grid View:** Visual 24-hour court schedule matrix for quick status verification.
- **Staff & User Role Directory:** View organization members and roles (`OrganizationOwner`, `OrganizationAdmin`, `OrganizationStaff`) under `/Admin/Users`.
- **Organization Settings & Custom Branding:** Update venue name, description, contact details, address, map coordinates, and upload custom venue logo.

### 7. Platform Administration (SaaS Owner)
- **Platform Admin Role:** Manage the entire multi-tenant ecosystem.
- **Organization Management:** Provision new tenant clubs, assign custom subdomains/slugs, activate/deactivate organizations, and assign owners.
- **Owner Account Activation Flow:** Secure single-use Identity activation links (`/Account/Activate`) allowing new venue owners to set their passwords directly without plaintext credentials.
- **Subscription Plans & Limits:** Define subscription tiers (Trial, Basic, Pro, Enterprise) with max court limits, max staff limits, and billing periods.
- **Subscription Assignment:** Manually assign, activate, suspend, or renew tenant subscriptions.
- **Subscription Wall:** Automated middleware enforcement preventing expired venues from accepting customer bookings while providing clear renewal banners.

### 8. UI & Interaction Design Polish
- **Floating Toast Notifications:** Instant user feedback for actions (e.g., booking confirmed, settings saved, payment verified).
- **Accessible Modal Confirmations:** Modern Bootstrap modal replacing standard native browser dialogs for destructive actions (e.g., cancel booking, deactivate court).
- **Interactive Loading States:** Per-button loading spinners and page loading overlays prevent accidental double-submits.
- **Responsive Mobile Layouts:** Fluid design optimized for smartphones, tablets, and desktops with sticky summary sidebar and mobile-friendly slot pickers.

---

## Technical Stack

| Component | Technology |
| :--- | :--- |
| **Framework** | ASP.NET Core (.NET 10) |
| **Language** | C# 14 |
| **UI** | Razor Pages, Vanilla CSS Design System, Bootstrap 5, Bootstrap Icons |
| **Database** | PostgreSQL via Entity Framework Core & Npgsql |
| **Authentication** | ASP.NET Core Identity with role-based policies |
| **File Storage** | Supabase S3 Object Storage (Public court images, Private payment proofs) |
| **Notifications** | MailKit / MimeKit (SMTP), Semaphore API (SMS), Telegram Bot API |
| **Testing** | xUnit, Moq, EF Core InMemory & PostgreSQL Integration Tests |
| **Production Server** | Ubuntu Linux VPS, Nginx Reverse Proxy, Systemd, Let's Encrypt Wildcard SSL |

---

## Project Structure

```
PickleBallBooking/
├── deploy/                           # VPS deployment scripts, SSL hooks, and Nginx configs
│   ├── Deploy-ToVPS.ps1              # Windows automated build, SCP, and remote deploy script
│   ├── setup-vps.sh                  # Ubuntu server provisioning (.NET 10, Nginx, UFW)
│   ├── deploy-app.sh                 # Remote application installation & service restart
│   ├── nginx-punitbola.conf          # Wildcard reverse proxy configuration
│   └── pikolball.service             # Systemd service unit definition
├── PickleBallBooking/                # Main application project
│   ├── Data/                         # ApplicationDbContext, migrations, and seed data
│   ├── Models/                       # Domain models (Booking, Court, Activity, Match, PlayerProfile)
│   ├── Pages/                        # Razor Pages
│   │   ├── Account/                  # Authentication, registration, and activation
│   │   ├── Activities/               # Public activity catalog, RSVP, and session details
│   │   ├── Admin/                    # Venue & platform administrative portals
│   │   │   ├── Activities/           # Activity and recurring series management
│   │   │   ├── CheckIn/              # On-site player attendance and QR scanning
│   │   │   ├── CourtAssignment/      # Real-time court allocation
│   │   │   ├── Courts/               # Court configuration and image uploads
│   │   │   ├── Matches/              # Match scoring and verification
│   │   │   ├── Organizations/        # Platform Admin: tenant provisioning
│   │   │   ├── OrgSettings/          # Venue branding and GCash payment setup
│   │   │   ├── Payments/             # Payment verification and screenshot review
│   │   │   ├── Players/              # Walk-in player registration and roster directory
│   │   │   ├── RoundRobin/           # Mixer tournament generator and match management
│   │   │   ├── Standings/            # Tournament and league leaderboards
│   │   │   └── Subscriptions/        # Platform Admin: subscription plan management
│   │   ├── Availability/             # Public court availability grid
│   │   ├── Booking/                  # 4-step booking workflow, price calculation, payment lookup
│   │   ├── Player/                   # Player profile, career statistics, and match history
│   │   └── Shared/                   # Layouts, navigation, toasts, and partial views
│   ├── Services/                     # 60+ modular business logic services
│   └── wwwroot/                      # CSS design system, JavaScript libraries, and static assets
└── PickleBallBooking.Tests/          # Automated test suite (438 unit and integration tests)
```

---

## Getting Started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL (Local instance or [Supabase](https://supabase.com/))
- Git

### Installation & Local Setup

1. **Clone the repository:**
   ```bash
   git clone https://github.com/eraser67/pikolball-booking.git
   cd pikolball-booking
   ```

2. **Configure User Secrets:**
   ```bash
   cd PickleBallBooking
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=pikolball;Username=postgres;Password=your_password;"
   dotnet user-secrets set "Supabase:Url" "https://your-project.supabase.co"
   dotnet user-secrets set "Supabase:ServiceRoleKey" "your-service-role-key"
   dotnet user-secrets set "Email:SmtpHost" "smtp.gmail.com"
   dotnet user-secrets set "Email:SmtpPort" "587"
   dotnet user-secrets set "Email:SmtpUser" "your-email@gmail.com"
   dotnet user-secrets set "Email:SmtpPass" "your-app-password"
   dotnet user-secrets set "Email:SenderEmail" "notifications@punitbola.tech"
   dotnet user-secrets set "Email:SenderName" "Punit Bola"
   ```

3. **Apply Database Migrations:**
   ```bash
   dotnet ef database update
   ```

4. **Run the Application:**
   ```bash
   dotnet run
   ```
   Open `http://localhost:5093` in your browser. For tenant subdomain testing, map `demo.localhost` in your local `hosts` file.

---

## Testing

Run the full automated test suite:
```bash
dotnet test
```

> [!TIP]
> The test suite includes 438 tests covering:
> - Multi-tenant isolation and global query filter enforcement
> - Concurrent booking collision and timeslot continuity checks
> - Dynamic pricing and overnight crossing-midnight calculations
> - Round Robin round generation, partner distribution, and bye handling
> - Match scoring, win-by-2 rules, standings, and player statistics
> - Secure payment proof URL generation and GCash state machines

---

## Production Deployment

The platform is deployed on an Ubuntu Linux VPS with Nginx and Let's Encrypt wildcard SSL certificates.

### Automated Deployment Script
Deploy updates from your local development environment:
```powershell
.\deploy\Deploy-ToVPS.ps1 -VpsIp "187.127.223.93" -VpsUser "root" -Domain "punitbola.tech"
```

The script automatically:
1. Compiles and packages a release build (`dotnet publish -c Release`).
2. Transmits the payload to the server via SCP.
3. Updates `/var/www/pikolball` with strict file permissions.
4. Restarts `pikolball.service` and executes health checks against the local Kestrel daemon and public HTTPS endpoints.