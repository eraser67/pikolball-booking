# PICKLEBALL BOOKING SYSTEM - PROJECT REQUIREMENTS

## PROJECT OVERVIEW

A simple, maintainable, responsive multi-tenant web-based Pickleball Court Booking and Venue Management SaaS Platform using:

- **C#** and **ASP.NET Core** (.NET 10)
- **Razor Pages** for UI
- **Entity Framework Core** with PostgreSQL / Supabase
- **Bootstrap 5** & **Bootstrap Icons** with custom design tokens
- **ASP.NET Core Identity** for authentication and role-based access control
- **Supabase Storage** for court images, QR codes, and private payment proofs
- **MailKit / MimeKit** for transactional email notifications via SMTP
- **Nginx & systemd** for production Linux VPS hosting

**Architecture:** Monolithic Razor Pages application with scoped service layer, tenant resolution middleware, and database-level multi-tenancy.

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

# 1. TECHNOLOGY STACK & CONSTRAINTS

### Technology Stack:
- **Language:** C# 13 (.NET 10)
- **Web Framework:** ASP.NET Core Razor Pages
- **ORM:** Entity Framework Core 10 (Npgsql PostgreSQL Provider)
- **Database:** PostgreSQL (Supabase managed or self-hosted)
- **Frontend Styling:** Bootstrap 5.3 + Bootstrap Icons + Custom CSS design tokens (`site.css`)
- **Authentication:** ASP.NET Core Identity (cookie-based, salted hash passwords)
- **Object Storage:** Supabase Storage (S3-compatible REST API)
- **Email:** MailKit / MimeKit over SMTP (TLS / Port 587)
- **Hosting & Web Server:** Ubuntu Linux VPS, Nginx reverse proxy, systemd service

### Constraints (Do NOT Use / Out of Scope):
- No client-side SPA frameworks (React, Angular, Vue, Next.js).
- No Node.js backend or TypeScript build pipelines.
- No microservices architecture or message brokers (Kafka, RabbitMQ).
- No distributed caches (Redis) or container orchestrators (Kubernetes, Docker Swarm).
- No automated third-party payment gateway integrations (PayMongo, Xendit, Stripe, PayPal, automated GCash merchant API).
- No separate database per tenant (single shared database with `OrganizationId` tenant isolation).

---

# 2. APPLICATION SCOPE

### Customer Experience:
1. **Public Club Homepage:** Modern branded hero, court showcase, operating hours preview, FAQ accordion, location map, and direct booking CTA.
2. **Dynamic Booking Calendar:**
   - Court selector and calendar date picker.
   - Dynamic **AJAX slot loading** (`OnGetSlotsAsync`) when court or date changes without page reload or URL disruption.
   - All 24 fixed hourly TimeSlots (12:00 AM – 11:00 PM) displayed with real-time status badges (*Available*, *Booked*, *Maintenance*).
   - Automatically disabled past slots for the current date.
   - Continuous multi-timeslot selection (minimum 1 hour, continuous range).
   - **Interactive Gap Detection Alert:** Immediate warning banner (`"Cannot select time slot with gap."` with shake animation) when non-continuous slots are clicked.
3. **Two-Stage Booking & Calculation UX:**
   - Customer clicks **Calculate Price** to compute duration and price server-side.
   - Step 4 ("Your Information") remains hidden until price calculation completes.
   - On mobile devices, clicking Calculate Price smoothly auto-scrolls down to Step 4 and sets cursor focus to the Full Name input.
4. **Anonymous Reservation Submission:** Enter Full Name, Mobile Phone, and Email address (no account registration required).
5. **Unique Booking Reference:** Formatted per date and sequence (e.g. `PB-20260921-0001`), guaranteed collision-proof across tenants.
6. **Booking Confirmation & Self-Service Lookup:** Lookup status using Booking Reference + Phone/Email.
7. **Manual GCash Payment Submission:** View club's GCash QR code, account details, and instructions; enter reference number; optionally upload payment screenshot.

### Tenant Admin Experience (Club Owners & Staff):
1. **Secure Authentication:** Identity login with lockout and role authorization.
2. **Admin Dashboard:** Real-time metrics for today's bookings, pending payment verifications, monthly revenue in ₱, and active courts.
3. **Court Management:** Add/edit courts, upload court photos (via Supabase Storage), activate/deactivate.
4. **TimeSlot & Maintenance Control:** Set club bookable hours; flag specific court-hour pairs as Maintenance.
5. **Flexible Pricing Engine:** Configurable rates for weekdays vs. weekends, hourly time periods, and overnight ranges crossing midnight.
6. **Booking Management:** Filter by date, court, and status (Pending, Confirmed, Cancelled, Completed); approve or cancel reservations.
7. **Schedule Matrix:** 24-hour visual grid of court occupancy.
8. **Manual Payment Verification:** Inspect submitted reference numbers and uploaded payment proofs (via secure 5-minute signed URLs); verify or reject payments.
9. **Venue Customization & Branding:** Manage venue profile, address, contact numbers, map coordinates, and club logo.

### Platform Administration (SaaS Owner):
1. **Organization Lifecycle Management:** Create new club tenants, assign custom subdomains/slugs, activate/deactivate clubs.
2. **Subscription Plans & Tiers:** Configure plan limits (max courts, max staff, features, billing periods).
3. **Subscription Assignment & Gating:** Assign plans to organizations, monitor subscription status, and enforce subscription walls for expired accounts.

---

# 3. MULTI-TENANCY & SUBDOMAIN RESOLUTION

### 1. Tenant Abstraction
- The primary tenant entity is `Organization`.
- The subdomain of the request identifies the organization:
  ```
  https://demo.punitbola.tech  →  Organization slug: "demo"
  https://ace.punitbola.tech   →  Organization slug: "ace"
  ```

### 2. Subdomain Parsing Rules (`TenantHostParser`)
- Extracts the first segment before the root base domain (e.g., `punitbola.tech`).
- Supports local development formats (e.g., `demo.localhost` or ambient default fallback).
- **Reserved Subdomains:** The following subdomains must NEVER resolve to a tenant organization:
  `www`, `app`, `admin`, `api`, `mail`, `support`.

### 3. Tenant Isolation Principles
- Every tenant-owned table carries `OrganizationId`:
  - `Courts`
  - `Bookings`
  - `Pricing`
  - `CourtTimeSlots`
  - `Payments`
  - `OrganizationPaymentSettings`
  - `Subscriptions`
- **Global Query Filters:** EF Core automatically applies `.Where(e => e.OrganizationId == currentTenantId)` to all queries.
- `OrganizationId` is resolved server-side and is **never** accepted from customer forms, query strings, or request bodies.
- `IgnoreQueryFilters()` is restricted to:
  - Generating globally unique booking references across tenants.
  - PlatformAdmin cross-organization administration.
  - Looking up bookings and payments on public confirmation pages by unique reference number.

---

# 4. FIXED HOURLY TIMESLOTS & CONTINUOUS BOOKINGS

### 1. 24 Standard Fixed Hourly TimeSlots
Atomic unit of booking (12:00 AM – 1:00 AM through 11:00 PM – 12:00 AM).
- TimeSlots are global and uniform across all organizations.
- Operating hours and maintenance are configured per organization/court.

### 2. Continuous Multi-Hour Reservations
- Customers select one or more consecutive hourly TimeSlots.
- Creates **ONE** Booking record spanning `StartTime` to `EndTime`.
- Separate booking records per hour are strictly prohibited.
- Gaps between selected slots are rejected by both client-side validation and server-side checks.

### 3. Concurrency & Overlap Protection
- Double booking is prevented by:
  - Application-level transaction checks (`StartTime < requestedEnd && EndTime > requestedStart`).
  - Database exclusion constraints on court, date, and time span.
- Cancelled bookings immediately release TimeSlots for new reservations.
- Back-to-back bookings are fully supported (e.g., 18:00–19:00 and 19:00–20:00).

---

# 5. CURRENCY & PRICING RULES

### 1. Currency Standard
- The system standard currency is the **Philippine Peso (`₱`)**.
- Formatting conforms to the `en-PH` culture (`₱500.00` or `₱500`).
- Consistent presentation across customer booking pages, receipts, admin dashboard, and notification emails.

### 2. Server-Side Price Authority
- All prices displayed on the frontend are calculated and verified server-side.
- Pricing rules support:
  - Base hourly rates.
  - Weekday vs. Weekend rates.
  - Time period adjustments (e.g., Peak vs. Off-Peak).
  - Overnight pricing crossing midnight (e.g., 18:00 to 02:00 next day).

---

# 6. MANUAL GCASH PAYMENT WORKFLOW

### 1. Workflow Architecture
- **No Third-Party Gateway:** Operates as a manual proof-of-payment workflow without merchant gateway transaction fees.
- Venue owners configure their GCash details in `/Admin/Payments` settings:
  - Account Name
  - Account Number / Mobile Number
  - Instructions
  - Uploaded GCash QR Code Image

### 2. Customer Submission Flow
1. Booking is created in `Pending` status.
2. Customer is redirected to `/Booking/Payment` showing the venue's GCash details and QR code.
3. Customer transfers funds using their GCash mobile app.
4. Customer enters the GCash 13-digit Reference Number and optionally uploads a screenshot of the receipt.
5. Status transitions: `Pending` → `Submitted`.

### 3. Payment Proof Storage Security
- QR codes are stored in the public `court-images` Supabase bucket.
- Payment proof screenshots are stored in the **private** `payment-proofs` Supabase bucket with path:
  `payment-proofs/{organization-slug}/{booking-reference}/{filename}`.
- Proof images are strictly restricted to authorized tenant admins via short-lived signed URLs (5-minute expiration).
- File uploads are validated by MIME type, file extension, and magic bytes (JPEG, PNG, WebP) with a 1 MB size limit.

### 4. Admin Verification Flow
- Admins review pending payments in `/Admin/Payments`.
- Admin inspects the reference number and views the proof screenshot.
- **Verify:** Sets payment status to `Verified`, automatically confirms the booking (`Pending` → `Confirmed`), and dispatches confirmation email.
- **Reject:** Sets payment status to `Rejected` with explanatory notes and sends rejection email.
- **Security Rule:** Customers cannot self-verify payments.

---

# 7. TRANSACTIONAL EMAIL NOTIFICATIONS

- Implemented via `BookingEmailService` using `MailKit` / `MimeKit` over standard SMTP (supports Gmail App Passwords).
- Automatic triggers:
  1. **Customer Booking Created:** Booking details, total amount in ₱, and payment instructions.
  2. **Admin New Booking Alert:** Notice of new pending reservation.
  3. **Customer Payment Submitted:** Receipt acknowledgement.
  4. **Admin Payment Review Notice:** Alert that proof is ready for review.
  5. **Payment Verified & Booking Confirmed:** Official confirmation receipt.
  6. **Payment Rejected:** Explanation of issue and next steps.
  7. **Booking Cancellation:** Notification to both customer and club owner.
- Non-blocking execution: Email dispatch errors are logged without interrupting database operations.

---

# 8. SUBSCRIPTION MANAGEMENT & GATING

### 1. Subscription Plans
- Configurable subscription plans (`SubscriptionPlan`):
  - Plan Name (Free Trial, Starter, Pro Club, Enterprise)
  - Price and Billing Period (Monthly, Annual)
  - Max Courts limit
  - Max Staff limit
  - Feature flags

### 2. Subscription Statuses & Enforcement
- Statuses: `Trial`, `Active`, `Expired`, `Suspended`, `Cancelled`.
- **Gating Rules:**
  - If an organization's subscription is `Expired` or `Suspended`:
    - The customer booking engine blocks new reservations with a venue renewal notice.
    - Venue admins are redirected to `/Admin/SubscriptionRequired` with renewal instructions.
  - Court creation enforces the `MaxCourts` limit of the organization's active plan.

---

# 9. PRODUCTION VPS DEPLOYMENT SPECIFICATIONS

- **Server:** Hostinger Ubuntu Linux VPS (`187.127.223.93`)
- **App Service:** .NET 10 Kestrel running on local port 5000, managed as a systemd service (`pikolball.service`) with automated restart on failure.
- **Reverse Proxy:** Nginx configured with HTTP/2 and WebSocket proxying.
- **SSL Certificates:** Wildcard Let's Encrypt SSL (`*.punitbola.tech`, `punitbola.tech`) managed via Certbot.
- **Automated Deployment Pipeline:** PowerShell script (`deploy/Deploy-ToVPS.ps1`) to compile, archive, transfer, and deploy updates with zero database downtime.

---

# 10. CURRENT IMPLEMENTATION STATUS

- **Phases 1–15:** Core Single-Venue Application — **COMPLETE**
- **Phases 16–18:** Fixed Hourly TimeSlots & UI Polish — **COMPLETE**
- **Phases 19–23:** Multi-Tenant Foundation, Isolation, Subdomain Resolution & Branding — **COMPLETE**
- **Phases 24–27:** Manual GCash Payments, Emails, Subscriptions & Platform Admin — **COMPLETE**
- **Phases 28–30:** Security Audit, Production Readiness & Hostinger VPS Deployment — **COMPLETE**
- **Post-Phase 30 Polish:**
  - Multi-tenant reference sequence collision fix (`IgnoreQueryFilters()`) — **COMPLETE**
  - Dynamic AJAX Court & Date switching without page reload — **COMPLETE**
  - Real-time Timeslot Gap Error Alert banner — **COMPLETE**
  - Two-Stage Calculate & Responsive Mobile Auto-Scroll UX — **COMPLETE**
  - Philippine Peso (`₱`) Currency Standardization — **COMPLETE**
- **Automated Test Suite:** **228 Passing Tests** (`dotnet test --filter "FullyQualifiedName!~Postgres"`)
