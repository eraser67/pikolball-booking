# Pickleball Booking System (Pikolball SaaS)

A simple, maintainable, responsive multi-tenant web-based Pickleball Court Booking and Venue Management System.

Built with:
- **C#** and **ASP.NET Core** (.NET 10)
- **Razor Pages** for UI
- **Entity Framework Core** with PostgreSQL / Supabase
- **Bootstrap 5** & **Bootstrap Icons** with custom brand design system
- **ASP.NET Core Identity** for authentication and role management
- **Supabase Storage** for court images and private payment proof uploads
- **MailKit / MimeKit** for SMTP transactional emails
- **Nginx** & **systemd** for production deployment on Linux VPS (Ubuntu)

**Status:** Phases 1–30 Complete + Production Deployed (`punitbola.tech` / `demo.punitbola.tech`)

---

## Overview & Architecture

Pikolball is a full-featured multi-tenant SaaS application designed for sports venue operators and pickleball clubs. Each organization operates on its own dedicated subdomain (e.g., `demo.punitbola.tech`) with isolated data, custom branding, personalized payment instructions, and role-based management.

```
                    Internet / Customer / Admin
                                ↓
                 Nginx Reverse Proxy + Let's Encrypt SSL
                                ↓
                     ASP.NET Core (.NET 10)
                                ↓
┌───────────────────────────────┴───────────────────────────────┐
│ Middleware & Services:                                        │
│  - TenantResolutionMiddleware (Subdomain → Organization)      │
│  - SubscriptionWallMiddleware (Active / Expired Enforcement)  │
│  - ITenantContext / Scoped Service Layer                      │
│  - BookingService, CourtService, PricingService               │
│  - PaymentService (GCash Manual + Proof Upload)               │
│  - BookingEmailService (SMTP / Gmail Notifications)           │
│  - SupabaseStorage (Public QR/Courts & Private Proofs)        │
└───────────────────────────────┬───────────────────────────────┘
                                ↓
                      Entity Framework Core
             (Global Query Filters by OrganizationId)
                                ↓
                 PostgreSQL (Supabase / Self-Hosted)
```

---

## Features

### Customer Experience

- **Branded Homepage:** Dynamic hero, court showcase with availability preview, how it works, FAQ accordion, and venue details.
- **Dynamic Calendar-Style Booking:**
  - Fast court & date switching via **AJAX** without page refresh or URL disruption (`OnGetSlotsAsync`).
  - View all 24 fixed hourly TimeSlots (12:00 AM – 11:00 PM) with real-time status badges (*Available*, *Booked*, *Maintenance*).
  - Past slots disabled automatically for current date.
- **Continuous Multi-TimeSlot Selection:** Select consecutive hours for a single reservation.
- **Interactive Timeslot Gap Error Alert:** Real-time error warning (`"Cannot select time slot with gap."` with shake animation) when non-consecutive slots are clicked.
- **Two-Phase Booking UX:**
  - Review court, duration, and server-calculated price in Philippine Peso (`₱`).
  - Step 4 ("Your Information") is strictly revealed after clicking **Calculate Price**.
  - On mobile devices, clicking Calculate Price triggers a smooth auto-scroll to Step 4 and focuses the Full Name input.
- **Anonymous Booking:** No customer login required; enter Name, Phone, and Email at reservation time.
- **Unique Booking Reference:** Formatted per organization date (e.g., `PB-20260921-0001`) with multi-tenant collision-proof sequence generation.
- **Self-Service Booking Lookup:** Customers can search reservations using their booking reference and email/phone without logging in.
- **Manual GCash Payment Workflow:**
  - View organization-specific GCash account name, number, QR code, and payment instructions.
  - Submit GCash transaction reference number and optionally upload payment screenshot proof.
  - Automatic status updates on payment verification.

### Tenant Admin Experience

- **Dedicated Venue Admin Portal:** Scoped to the authenticated organization admin.
- **Interactive Dashboard:** Summary cards for daily bookings, pending verifications, monthly revenue, and active courts.
- **Court Management:** Add, edit, upload court images (via Supabase Storage), activate, and deactivate courts.
- **TimeSlot & Maintenance Management:** Configure bookable operating hours and flag individual courts/hours for maintenance.
- **Flexible Pricing Engine:**
  - Different rates for weekdays vs. weekends.
  - Multiple time period tiers (e.g., peak evening vs. off-peak morning).
  - Overnight pricing support crossing midnight (e.g., 6:00 PM – 2:00 AM).
- **Booking Management:** Search, filter by date/court/status, confirm pending reservations, cancel bookings, and view complete history.
- **Schedule Grid View:** Visual 24-hour court schedule matrix for quick status verification.
- **Manual Payment Verification:**
  - View submitted customer GCash reference numbers and inspect uploaded payment proof screenshots using secure short-lived signed URLs.
  - Verify payments (automatically confirms the booking and emails the customer) or reject with explanatory notes.
- **Organization Settings & Custom Branding:** Update venue name, description, contact details, address, map coordinates, and upload custom venue logo.

### Platform Administration (SaaS Owner)

- **Platform Admin Role:** Manage the entire multi-tenant ecosystem.
- **Organization Management:** Provision new tenant clubs, assign custom subdomains/slugs, activate/deactivate organizations, and assign owners.
- **Subscription Plans & Limits:** Define subscription tiers (Trial, Basic, Pro, Enterprise) with max court limits, max staff limits, and billing periods.
- **Subscription Assignment:** Manually assign, activate, suspend, or renew tenant subscriptions.
- **Subscription Wall:** Automated enforcement preventing expired venues from accepting customer bookings while providing clear renewal banners.

---

## Core Technical Highlights

### 1. Multi-Tenant Architecture & Subdomain Resolution
- Request hostnames are parsed by `TenantHostParser` to resolve tenant subdomains (e.g. `demo.punitbola.tech` maps to organization `demo`).
- Platform subdomains (`www`, `app`, `admin`, `api`, `mail`, `support`) are reserved and protected.
- Data isolation is enforced at the database level via EF Core Global Query Filters (`b => b.OrganizationId == currentTenantId`).
- `IgnoreQueryFilters()` is utilized strictly in server-authorized cross-tenant administrative tasks and unique reference sequence generation.

### 2. Fixed Hourly TimeSlots & Server-Side Authority
- 24 standardized 1-hour bookable units per day (00:00–01:00 through 23:00–00:00).
- One reservation = One continuous booking record with `StartTime` and `EndTime` (not separate records per hour).
- Strict server-side validation: client prices and availability states are never trusted; prices are recalculated and overlapping slots checked inside database transactions.
- Zero "dead time": back-to-back bookings are fully supported (e.g. 18:00–19:00 and 19:00–20:00).
- Immediate slot release upon booking cancellation.

### 3. Philippine Peso (`₱`) Currency Standardization
- Currency formatting is standardized across the entire application using the `en-PH` culture and explicit `₱` symbols.
- Clean formatting for whole and fractional amounts (`₱500` or `₱500.50`).

### 4. Storage & Media Management
- Dual-mode storage via Supabase S3-compatible object storage:
  - **Public Bucket (`court-images`):** Venue logos, court photos, and GCash QR codes.
  - **Private Bucket (`payment-proofs`):** Customer payment receipt screenshots, accessible only to tenant admins via short-lived signed URLs (5-minute expiry).

### 5. Email Notification System
- Powered by `MailKit` and `MimeKit` using SMTP (supports Gmail App Passwords and standard transactional SMTP relays).
- HTML & plain-text responsive email notifications:
  - Customer Booking Received & Payment Instructions
  - Venue Owner New Booking Notification
  - Payment Proof Submitted Alert
  - Payment Verified & Booking Confirmation Receipt
  - Payment Rejection Notice with Admin Feedback
  - Booking Cancellation Notices

---

## Project Structure

```
PickleBallBooking/
├── deploy/                        # VPS Deployment scripts and Nginx configs
│   ├── Deploy-ToVPS.ps1           # Windows PowerShell automated publish & deploy
│   ├── setup-vps.sh               # Initial VPS server provisioning (Ubuntu/.NET/Nginx)
│   ├── deploy-app.sh              # Remote VPS app extraction and restart script
│   ├── nginx-punitbola.conf       # Multi-tenant Nginx wildcard reverse proxy
│   ├── nginx-ssl.conf             # Production SSL configuration
│   └── pikolball.service          # Systemd service unit definition
├── PickleBallBooking/             # Main ASP.NET Core Razor Pages application
│   ├── Data/                      # DbContext, migrations, and seeders
│   ├── Models/                    # Entity models (Organization, Booking, Court, Payment, etc.)
│   ├── Pages/                     # Razor Pages UI
│   │   ├── Account/               # Authentication & account activation
│   │   ├── Admin/                 # Admin management & platform administration
│   │   │   ├── Courts/            # Court management & court image upload
│   │   │   ├── Organizations/     # Platform Admin: organization creation & oversight
│   │   │   ├── Payments/          # GCash verification & proof review
│   │   │   ├── Subscriptions/     # Subscription tier management & assignment
│   │   │   └── OrgSettings/       # Tenant branding & venue settings
│   │   └── Booking/               # Customer booking flow, AJAX slots, payment & lookup
│   ├── Services/                  # Business logic services & tenant context
│   │   ├── BookingService.cs      # Core booking, validation, and collision-proof sequence
│   │   ├── PaymentService.cs      # GCash workflow & proof coordination
│   │   ├── SubscriptionService.cs # Tenant subscription gating
│   │   ├── TenantHostParser.cs    # Subdomain resolution logic
│   │   └── BookingEmailService.cs # Transactional email templates & dispatch
│   └── wwwroot/                   # CSS (brand design system), JavaScript, and assets
└── PickleBallBooking.Tests/       # xUnit automated test suite (228+ unit tests)
```

---

## Local Development Quick Start

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Visual Studio 2026, VS Code, or JetBrains Rider
- PostgreSQL database (local or Supabase free tier)
- Git

### Setup Instructions

1. **Clone the repository:**
   ```bash
   git clone https://github.com/eraser67/pikolball-booking.git
   cd pikolball-booking
   ```

2. **Configure User Secrets:**
   ```bash
   cd PickleBallBooking
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=your-postgres-host;Port=5432;Database=postgres;Username=postgres;Password=your-password;"
   dotnet user-secrets set "Supabase:Url" "https://your-project.supabase.co"
   dotnet user-secrets set "Supabase:ServiceRoleKey" "your-service-role-key"
   dotnet user-secrets set "Email:SmtpHost" "smtp.gmail.com"
   dotnet user-secrets set "Email:SmtpPort" "587"
   dotnet user-secrets set "Email:SmtpUser" "your-email@gmail.com"
   dotnet user-secrets set "Email:SmtpPass" "your-app-password"
   dotnet user-secrets set "Email:SenderEmail" "your-email@gmail.com"
   dotnet user-secrets set "Email:SenderName" "Pikolball"
   ```

3. **Apply Migrations & Seed Default Data:**
   ```bash
   dotnet ef database update
   ```

4. **Run the Application:**
   ```bash
   dotnet run
   ```
   - Homepage: `http://localhost:5000`
   - Admin Login: `http://localhost:5000/Account/Login`

---

## Testing

Run the automated test suite:
```bash
dotnet test --filter "FullyQualifiedName!~Postgres"
```

The test suite covers:
- Continuous multi-hour booking validation and gap prevention
- Server-side pricing calculations (weekday, weekend, overnight crossing midnight)
- Overlap detection and concurrent booking protection
- Subdomain parsing and reserved slug routing
- Cross-tenant data isolation (database query filter verification)
- GCash payment state transitions and self-verification prevention
- Payment proof file upload and private bucket access validation
- Subscription wall enforcement and plan limit checks

---

## Production Deployment (Hostinger VPS)

The system is deployed in production on a Hostinger Ubuntu Linux VPS with Nginx and Let's Encrypt SSL.

- **Primary Domain:** `https://punitbola.tech`
- **Tenant Subdomains:** `https://*.punitbola.tech` (e.g., `https://demo.punitbola.tech`)
- **Server Architecture:** Nginx reverse proxy forwards traffic to Kestrel running locally on port 5000 managed by `systemd` (`pikolball.service`).

### Automated Deployment from Development Machine:
Run the deployment script from PowerShell:
```powershell
.\deploy\Deploy-ToVPS.ps1 -VpsIp "187.127.223.93" -VpsUser "root" -Domain "punitbola.tech"
```

This script automatically:
1. Builds and publishes a self-contained release bundle (`Release - net10.0`).
2. Packages the bundle into a compressed deployment archive.
3. Securely uploads the bundle to the VPS via SCP.
4. Executes `deploy-app.sh` on the VPS to update binaries, preserve environment files, run database migrations, and restart the `pikolball` service.

---

## License

MIT License