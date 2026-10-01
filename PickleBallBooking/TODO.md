# TODO - Pickleball Booking System

## Development Environment & Setup

- [x] Verify development environment (.NET 10, C# 13, Visual Studio / VS Code)
- [x] Create ASP.NET Core project
- [x] Configure GitHub (repository pushed to `origin/phase-19-multi-tenant`)
- [x] Configure Supabase (connection string via User Secrets & VPS environment; migrations applied)
- [x] Configure Supabase Storage (public `court-images` bucket and private `payment-proofs` bucket)
- [x] Configure MailKit / SMTP email provider (Gmail App Password support)

## Core Application (Phases 1–15)

- [x] Create database models (Court, TimeSlot, Pricing, Booking, CourtTimeSlot)
- [x] Create EF Core migrations
- [x] Build Court Management
- [x] Build Time Slot Management
- [x] Build Pricing Management
- [x] Build Customer Booking
- [x] Build Availability Engine
- [x] Prevent Double Booking (concurrency transactions & exclusion constraints)
- [x] Build Booking Lookup (by reference + phone/email)
- [x] Build Admin Authentication (ASP.NET Core Identity)
- [x] Build Admin Dashboard
- [x] Build Booking Management (Confirm, Cancel, Complete)
- [x] Build Schedule View (24-hour visual grid)
- [x] Add Automated Tests
- [x] Security Review (CSRF protection, input validation)

## Enhancement Phases (16–18)

- [x] **Phase 16 — Booking Model Redesign:** Fixed 1-hour TimeSlots as atomic unit; StartTime/EndTime ranges; single booking record per reservation.
- [x] **Phase 17 — Fixed-TimeSlot Availability UI:** Display 24 hourly slots; continuous range selection; duration & price calculation.
- [x] **Phase 18 — UI/UX Refinement:** Modern homepage (hero, court cards, FAQ, CTA); responsive navbar; Bootstrap Icons; CSS design tokens (`site.css`).

## Multi-Tenant SaaS Phases (19–30)

- [x] **Phase 19 — UI Polish:** Toast notifications, reusable confirmation modals, empty states, validation styling.
- [x] **Phase 20 — Multi-Tenant Database Foundation:**
  - [x] Add `Organization` model (Slug, Name, Branding, Address, Coordinates)
  - [x] Add `OrganizationMember` model (Roles: PlatformAdmin, OrganizationOwner, OrganizationAdmin, OrganizationStaff)
  - [x] Add `OrganizationId` foreign key to tenant-owned entities (Courts, Bookings, Pricing, CourtTimeSlots)
  - [x] Migrate existing data into Organization #1 ("Pikolball")
  - [x] Migration `20260919042146_AddMultiTenantFoundation` applied
- [x] **Phase 21 — Tenant Context & Isolation:**
  - [x] `ITenantContext` / request-scoped `TenantContext`
  - [x] `TenantResolutionMiddleware`
  - [x] EF Core Global Query Filters (`OrganizationId`)
  - [x] `TenantAdminAuthorization` policy and handler
  - [x] Cross-tenant automated isolation tests
- [x] **Phase 22 — Subdomain Tenant Resolution:**
  - [x] Subdomain strategy (`{slug}.punitbola.tech`)
  - [x] `TenantHostParser`: extract and validate tenant slug from hostname
  - [x] Reserved subdomains (`www`, `app`, `admin`, `api`, `mail`, `support`)
  - [x] Configurable `TenantOptions` with local development fallback
- [x] **Phase 23 — Organization Management & Branding:**
  - [x] Platform Admin: create organization + initial owner account
  - [x] Platform Admin: list / activate / deactivate organizations (`/Admin/Organizations`)
  - [x] Organization Owner account activation workflow (`/Account/Activate`)
  - [x] Tenant Admin: venue settings & logo management (`/Admin/OrgSettings`)
  - [x] Dynamic customer booking page branded with tenant name and logo
- [x] **Phase 24 — Manual GCash Payment:**
  - [x] `Payment` and `OrganizationPaymentSettings` models
  - [x] Venue admin GCash configuration (account name, number, instructions, QR code upload)
  - [x] Customer payment page (`/Booking/Payment`) with QR display and reference submission
  - [x] Payment proof screenshot upload with MIME and magic-byte validation
  - [x] Supabase Storage integration (private `payment-proofs` bucket)
  - [x] Tenant admin payment verification portal (`/Admin/Payments`) with secure 5-minute signed URLs
  - [x] Admin verify (auto-confirms booking) and reject actions
- [x] **Phase 25 — Transactional Email Notifications:**
  - [x] `BookingEmailService` and `SmtpEmailService` via `MailKit`
  - [x] Customer booking received & payment instructions email
  - [x] Venue owner new booking alert email
  - [x] Payment submitted notification
  - [x] Payment verified & booking confirmation email
  - [x] Payment rejection notice email
  - [x] Booking cancellation notices
- [x] **Phase 26 — Subscription Management & Gating:**
  - [x] `SubscriptionPlan` and `Subscription` models
  - [x] Statuses: `Trial`, `Active`, `Expired`, `Suspended`, `Cancelled`
  - [x] `SubscriptionService` and `SubscriptionWallMiddleware`
  - [x] Booking gate: block new reservations for expired/suspended venues
  - [x] Court limit gate: prevent adding courts beyond plan allowance
  - [x] `/Admin/SubscriptionRequired` warning page
- [x] **Phase 27 — Platform Administration:**
  - [x] Platform Admin dashboard and navigation
  - [x] Organization management (`/Admin/Organizations`)
  - [x] Subscription plan management (`/Admin/Subscriptions/Plans`)
  - [x] Subscription assignment and renewal (`/Admin/Subscriptions/Assign`)
- [x] **Phase 28 — Security & Tenant Isolation Audit:**
  - [x] Cross-tenant read and write tests
  - [x] Authorization policy tests
  - [x] Tampered URL and input tests (`OrganizationId` forgery prevention)
  - [x] Private payment proof bucket isolation tests
  - [x] 228 automated unit and integration tests passing
- [x] **Phase 29 — Final UI/UX & Production Readiness:**
  - [x] Responsive layout refinements across desktop, tablet, and mobile
  - [x] Custom error handling (`/Error`) with HTTP status code support
  - [x] Production settings (`appsettings.Production.json`)
- [x] **Phase 30 — Production VPS Deployment:**
  - [x] Hostinger Ubuntu VPS provisioning (`187.127.223.93`)
  - [x] Nginx reverse proxy with wildcard Let's Encrypt SSL (`*.punitbola.tech`)
  - [x] Systemd daemon service (`pikolball.service`)
  - [x] Automated deployment scripts (`deploy/Deploy-ToVPS.ps1`, `deploy/deploy-app.sh`, `deploy/setup-vps.sh`)
  - [x] Production verification on `https://punitbola.tech` and `https://demo.punitbola.tech`

## Post-Phase 30 Enhancements & Booking UX Polish

- [x] **Multi-Tenant Booking Reference Sequence Fix:**
  - Added `IgnoreQueryFilters()` to booking reference generation sequence query to prevent cross-tenant collisions on identical calendar dates.
- [x] **Interactive Timeslot Gap Error Alert:**
  - Implemented real-time dynamic error banner (`"Cannot select time slot with gap."` with shake animation) when selecting non-continuous slots.
- [x] **Two-Stage Calculate & Mobile Auto-Scroll UX:**
  - Strictly hide Step 4 ("Your Information") until clicking **Calculate Price**.
  - On mobile, automatically smooth-scroll down to Step 4 upon calculation and place cursor focus in the Full Name input field.
- [x] **Dynamic Court & Date Selection via AJAX:**
  - Load available/booked slots asynchronously via `OnGetSlotsAsync` on court or date changes without page reload or link refresh.
- [x] **Philippine Peso (`₱`) Currency Standardization:**
  - Standardize all currency rendering across frontend, admin pages, and transactional email templates to use the Philippine Peso (`₱`) symbol with `en-PH` formatting.

## Completed Community Foundation Phases (31–36)

- [x] **Phase 31 — Customer Account Registration & Authentication:** Self-registration, Name/Email/Password/Mobile, Customer role, platform-level identity.
- [x] **Phase 32 — Player Account & Profile Foundation:** PlayerProfile model, Avatar upload, Skill Level, Player Dashboard, Privacy Settings.
- [x] **Phase 33 — Open Play / Activities:** Admin-created events, Activity States, Court allocations, Public Activities browse directory.
- [x] **Phase 34 — RSVP & Waitlist:** ActivityRsvp model, RSVP states (Confirmed/Waitlisted/Cancelled), Waitlist ordering, Automatic promotion upon cancellation, Admin manual promote & reorder, Player dashboard activities table, Transactional email notifications.
- [x] **Phase 35 — Recurring Activities:** RecurrenceType, ActivitySeriesStatus, ActivitySeries models, Series CRUD & UI, automatic occurrence generation up to 2 years / 200 occurrences, occurrence cancellation / pause / resume / edit future, occurrence back-links.
- [x] **Phase 36 — Notifications:** In-app notification system (`AppNotification`, `AppNotificationType`, `AppNotificationService`), player notification feed (`/Customer/Notifications`), navbar unread badge with real-time count, Customer Dashboard quick links, automatic triggers for RSVP confirmed, waitlisted, waitlist promoted, RSVP cancelled, activity cancelled (bulk email & in-app alerts), admin email & Telegram alerts.

## Future Phases (37–61) — PLANNED

The long-term community and competitive feature roadmap is documented in [`COMMUNITY_ROADMAP.md`](COMMUNITY_ROADMAP.md).

All items below are **PLANNED / FUTURE**:
- [ ] **Phase 37 — Calendar Integration** *(ICS, Google Calendar, Apple Calendar, Outlook)*
- [ ] **Phase 38 — Player Check-In** *(QR Check-In, Admin Check-In, No-Show tracking, Attendance history)*
- [ ] **Phase 39 — Court Assignment** *(Manual, Automatic, Skill-Based grouping, Lock Assignment)*
- [ ] **Phase 40 — Round Robin Engine** *(Rotating Partners, Fixed Partners, Singles, Match schedule generation)*
- [ ] **Phase 41 — Match Scoring** *(Score entry, Live score, Admin finalization, Score correction with audit)*
- [ ] **Phase 42 — Standings & Leaderboards** *(Wins, Losses, Win %, Point differential, Tenant-scoped)*
- [ ] **Phase 43 — Player Statistics & Match History** *(Games played, Partner/Opponent history, Privacy controls)*
- [ ] **Phase 44 — Tournament Foundation** *(Single Elimination, Round Robin, Pool Play, Divisions)*
- [ ] **Phase 45 — Tournament Registration** *(Individual/Team, GCash payment integration, Waitlist)*
- [ ] **Phase 46 — Tournament Brackets** *(Seeding, Bracket generation, Match advancement, Finalization)*
- [ ] **Phase 47 — Tournament Scheduling** *(Court assignment, Conflict detection, Rest periods)*
- [ ] **Phase 48 — League Management** *(Seasons, Divisions, Teams, Schedule, Standings, Playoffs)*
- [ ] **Phase 49 — Community Feed** *(Admin announcements, Event/Tournament/Venue notices, Results)*
- [ ] **Phase 50 — Player Connections** *(Follow players, View profiles, Invite players, Privacy controls)*
- [ ] **Phase 51 — Kudos / Recognition** *(Great Partner, Good Sportsmanship — with anti-abuse controls)*
- [ ] **Phase 52 — Community Leaderboards** *(Most Active, Most Games, Most Events — separate from competitive)*
- [ ] **Phase 53 — Achievements / Badges** *(First Game, 10 Wins, Tournament Champion, Community Regular)*
- [ ] **Phase 54 — DUPR Integration** *(Match submission to DUPR, Player opt-in, Tenant isolation — external API required)*
- [ ] **Phase 55 — Advanced Notifications** *(In-App feed, Browser Push, Per-category preferences)*
- [ ] **Phase 56 — Find a Game (Game Discovery)** *(Location, Date, Skill, Format filters — uses Phase 31 platform account)*
- [ ] **Phase 57 — Multi-Tenant Community Marketplace** *(Platform-wide opt-in discovery, Tenant data isolation)*
- [ ] **Phase 58 — Advanced Tenant Community Controls** *(Feature flags per tenant: profiles, tournaments, DUPR, etc.)*
- [ ] **Phase 59 — Community Analytics** *(Booking, Community, Competitive analytics dashboards)*
- [ ] **Phase 60 — Advanced Match Scheduling** *(Constraint-based scheduling engine — future optimization)*
- [ ] **Phase 61 — PWA / Mobile Experience** *(Installable app, Push notifications, QR scanning, Mobile scoring)*


