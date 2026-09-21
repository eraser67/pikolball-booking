# Pickleball Booking System Development Plan

**Overall Status:** Phases 1–30 Complete + Deployed to Production (`punitbola.tech` / `demo.punitbola.tech`)

---

## Completed Core Phases (1–15)

### Phase 1 — Environment and project setup
- Initial ASP.NET Core project setup (.NET 10, C#, Razor Pages).
- Git repository initialization and version control setup.

### Phase 2 — PostgreSQL / Supabase configuration
- Connection to PostgreSQL via Supabase.
- User Secrets configuration for secure connection string handling.

### Phase 3 — Entity Framework Core models and migrations
- Core entities: Court, TimeSlot, Booking, Pricing, CourtTimeSlot.
- Database context configuration and initial migrations.

### Phase 4 — Court Management
- Admin management for courts (Add, Edit, Activate, Deactivate).
- Court status handling and data preservation.

### Phase 5 — Time Slot Management
- 24 fixed hourly TimeSlots (12:00 AM – 11:00 PM).
- Dynamic activation/deactivation of bookable operating hours.

### Phase 6 — Pricing Management
- Configurable pricing rules by weekday/weekend and time period.
- Overnight pricing support for ranges crossing midnight.

### Phase 7 — Customer Booking
- Public customer booking interface.
- Selection of court, date, and continuous TimeSlots.
- Anonymous reservation submission (Name, Phone, Email).

### Phase 8 — Availability Display
- Real-time display of slot availability (Available, Booked, Maintenance).
- Date and court selection integration.

### Phase 9 — Double Booking Protection
- Concurrency and overlap prevention.
- Atomic database transactions to prevent race conditions.

### Phase 10 — Booking Lookup
- Public self-service lookup by Booking Reference and Email/Phone.
- Secure lookup without requiring customer account registration.

### Phase 11 — Admin Authentication
- ASP.NET Core Identity integration.
- Secure login, password hashing, and cookie-based authentication.

### Phase 12 — Admin Dashboard
- Management dashboard with booking statistics, recent reservations, and quick links.

### Phase 13 — Booking Management
- Admin booking management: filter by date, court, and status.
- Workflow actions: Confirm, Cancel, Complete.

### Phase 14 — Schedule/Calendar View
- Grid-based visual calendar showing 24-hour court reservations.

### Phase 15 — Security and Error Handling
- Server-side input validation, anti-forgery tokens (CSRF protection), error logging, and custom error pages.

---

## Enhancement Phases (16–18)

### Phase 16 — Booking Model Redesign to Fixed Hourly TimeSlots
- **Objective:** Redesign the booking system to use fixed 1-hour TimeSlots as the atomic unit.
- Re-architected Booking model to store `BookingDate`, `StartTime`, `EndTime`, `DurationHours`, and `Price`.
- One Booking record per reservation (NOT one per hour).
- Concurrency exclusion constraints and data preservation migrations.

### Phase 17 — Fixed-TimeSlot Availability UI
- **Objective:** Display all 24 hourly TimeSlots with clear availability status.
- Color-coded badges: Available (green), Booked (red), Maintenance (gray).
- Continuous range selection enforcement (no gaps allowed).
- Client-side and server-side duration and price calculation.

### Phase 18 — UI/UX Refinement
- **Objective:** Complete aesthetic overhaul using Bootstrap 5 and modern design tokens.
- Modern branded homepage: hero section, dynamic court cards with availability previews, how it works, benefits, FAQ accordion, and footer.
- Enhanced responsive navigation with logo, customer links, and role-gated admin dropdown.
- Integrated Bootstrap Icons and CSS design system (`wwwroot/css/site.css`).
- Redesigned schedule view and uniform status badge indicators.

---

## Multi-Tenant SaaS Phases (19–30)

### Phase 19 — UI Polish & Notification Enhancements (Complete)
- Inline alerts converted to modern toast notifications and consistent alerts.
- Reusable confirmation modal replacing native browser dialogs for destructive actions.
- Empty states for tables, courts, bookings, and payments.
- Polished validation feedback and responsive spacing.

### Phase 20 — Multi-Tenant Database Foundation (Complete)
- **Objective:** Establish the multi-tenant database schema.
- Added `Organization` model: Id, Name, Slug (unique), Description, LogoUrl, Phone, Email, Address, Latitude, Longitude, TimeZone, Currency, Status.
- Added `OrganizationMember` model with roles: `PlatformAdmin`, `OrganizationOwner`, `OrganizationAdmin`, `OrganizationStaff`.
- Added `OrganizationId` foreign key to tenant-owned entities: `Court`, `Booking`, `Pricing`, `CourtTimeSlot`.
- Data migration: created Organization #1 ("Pikolball") and assigned existing courts, bookings, and pricing.
- Added global 24 TimeSlot definitions shared across all tenants.

### Phase 21 — Tenant Context & Server-Side Isolation (Complete)
- **Objective:** Enforce server-side tenant isolation in queries and business logic.
- Implemented `ITenantContext` and request-scoped `TenantContext`.
- Added EF Core Global Query Filters: queries automatically filtered by ambient `OrganizationId`.
- Added `TenantAdminAuthorization` requirement and handler ensuring admins can only access their own organization's records.
- Comprehensive cross-tenant tests validating zero cross-tenant leakage.

### Phase 22 — Subdomain Tenant Resolution (Complete)
- **Objective:** Resolve tenant from the incoming request hostname.
- Built `TenantHostParser` supporting `{slug}.punitbola.tech` and localhost subdomains (e.g., `demo.localhost`).
- Reserved platform subdomains: `www`, `app`, `admin`, `api`, `mail`, `support`.
- Safe local development resolution fallback with configurable `TenantOptions`.

### Phase 23 — Organization Management & Branding (Complete)
- **Objective:** Allow venue operators to customize venue branding and manage club details.
- Platform Admin management: create organizations, list, activate, and deactivate organizations.
- Organization Owner account activation workflow (`/Account/Activate`).
- Tenant Admin settings page (`/Admin/OrgSettings`): manage venue name, logo upload, contact details, and location coordinates.
- Customer booking interface dynamically styled with current tenant's name and branding.

### Phase 24 — Manual GCash Payment Workflow (Complete)
- **Objective:** Implement manual GCash payment flow with admin verification (no third-party payment gateway required).
- Added `Payment` and `OrganizationPaymentSettings` models.
- Venue owners configure GCash account name, number, instructions, and QR code upload.
- Customer payment page (`/Booking/Payment`): displays venue GCash QR, allows entering reference number, and optional payment proof screenshot upload.
- Storage integration via Supabase Storage:
  - QR codes stored in public bucket `court-images`.
  - Payment proofs stored in private bucket `payment-proofs`, isolated by tenant slug and booking reference.
- Admin payment portal (`/Admin/Payments`): inspect reference numbers, view proofs via secure short-lived signed URLs, verify payments (auto-confirms booking), or reject with reason notes.

### Phase 25 — Transactional Email Notifications (Complete)
- **Objective:** Send branded email notifications for booking and payment lifecycle events.
- Built `BookingEmailService` using `MailKit` and `MimeKit` over SMTP (supports Gmail App Passwords and transactional relays).
- HTML and plain-text responsive email notifications:
  - Customer Booking Received & Payment Instructions
  - Venue Owner New Booking Alert
  - Customer Payment Submitted Notice
  - Venue Owner Payment Review Notification
  - Payment Verified & Booking Confirmation Receipt
  - Payment Rejected Notice
  - Booking Cancellation Notices
- Error handling: email dispatch failures log gracefully without interrupting database transactions.

### Phase 26 — Subscription Management & Gating (Complete)
- **Objective:** Multi-tenant SaaS subscription plans and venue limits.
- Added `SubscriptionPlan` and `Subscription` models with statuses (`Trial`, `Active`, `Expired`, `Suspended`, `Cancelled`).
- Built `SubscriptionService` and `SubscriptionWallMiddleware`.
- Feature gating:
  - Max court limits per subscription plan.
  - Expired or suspended organizations cannot accept new bookings.
  - Dedicated `/Admin/SubscriptionRequired` warning page with renewal instructions.
- Seeded default plans: Free Trial, Starter, Pro Club, and Enterprise.

### Phase 27 — Platform Administration (Complete)
- **Objective:** High-level platform owner controls.
- `/Admin/Organizations`: View all clubs, status, court counts, and owners.
- `/Admin/Organizations/Create`: Create new club with initial owner and plan assignment.
- `/Admin/Subscriptions`: Global subscription overview and manual plan assignment/renewal.
- `/Admin/Subscriptions/Plans`: Create, edit, and activate/deactivate subscription plans.

### Phase 28 — Security & Tenant Isolation Audit (Complete)
- **Objective:** Full audit and validation of multi-tenant security boundaries.
- Over 228 automated unit and integration tests passing.
- Verified:
  - Tenant A cannot view or edit Tenant B bookings, courts, or settings.
  - Customer input cannot tamper with `OrganizationId`.
  - Private payment proofs bucket is inaccessible to unauthorized tenants.
  - Secure signed URLs expire after 5 minutes.

### Phase 29 — Final UI/UX & Production Readiness (Complete)
- Polished responsive layouts for mobile, tablet, and desktop viewports.
- Enhanced error handling (`/Error` page with status code handling: 400, 403, 404, 500).
- Production environment configurations (`appsettings.Production.json`).

### Phase 30 — Production VPS Deployment (Complete)
- **Objective:** Deploy multi-tenant platform to production Hostinger Ubuntu VPS.
- Configured Hostinger VPS (`187.127.223.93`) with Ubuntu, .NET 10 runtime, Nginx, and Certbot.
- Configured Nginx reverse proxy with wildcard Let's Encrypt SSL (`*.punitbola.tech` and `punitbola.tech`).
- Configured `systemd` daemon service (`pikolball.service`) with automatic restart on failure.
- Automated deployment scripts in `deploy/`:
  - `Deploy-ToVPS.ps1`: Windows PowerShell automated build, bundle, SCP upload, and remote deployment.
  - `setup-vps.sh`: Server bootstrap script.
  - `deploy-app.sh`: Remote extraction, permission setup, and service restart script.
- Live URL: `https://punitbola.tech` and `https://demo.punitbola.tech`.

---

## Post-Phase 30 Enhancements & Booking UX Polish

The following features were introduced following the production deployment:

1. **Multi-Tenant Booking Reference Sequence Fix:**
   - Isolated booking reference generation (`PB-YYYYMMDD-XXXX`) using `IgnoreQueryFilters()` to prevent cross-tenant sequence collisions and duplicate key exceptions when multiple organizations create bookings on the same calendar day.

2. **Interactive Timeslot Gap Error Alert:**
   - Real-time client-side error banner (`"Cannot select time slot with gap."` with a shake animation) displayed instantly when a user clicks non-adjacent timeslots.

3. **Two-Stage Calculate & Responsive Mobile Auto-Scroll:**
   - Step 4 ("Your Information") remains hidden until the user clicks **Calculate Price**.
   - Upon clicking Calculate Price, the system calculates duration and price, displays the breakdown, reveals Step 4, and on mobile viewports automatically smooth-scrolls to Step 4 while placing cursor focus into the Full Name field.

4. **Dynamic Court & Date Selection via AJAX:**
   - Switching courts or dates fetches updated available/booked slots asynchronously via `OnGetSlotsAsync` without a full page reload or link refresh, preserving user selection context and eliminating jarring page jumps.

5. **Philippine Peso (`₱`) Currency Standardization:**
   - Standardized all currency rendering across frontend views, admin reports, and email templates using the Philippine Peso (`₱`) symbol and `en-PH` formatting.

---

## Test Verification Summary

- Total Automated Tests: **228 Passing Tests**
- Test Framework: **xUnit**
- Test Coverage Areas:
  - Booking validation and continuous slot logic
  - Pricing engine (weekday, weekend, overnight crossing midnight)
  - Subdomain resolution & reserved slugs
  - Tenant context & query filter enforcement
  - Payment lifecycle (submission, verification, rejection)
  - Payment proof storage & MIME-type validation
  - Subscription status gating and limits
