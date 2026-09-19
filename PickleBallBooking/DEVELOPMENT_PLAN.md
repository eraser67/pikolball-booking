# Pickleball Booking System Development Plan

## Completed Phases (1–15)

### Phase 1
Environment and project setup

### Phase 2
PostgreSQL / Supabase configuration

### Phase 3
Entity Framework Core models and migrations

### Phase 4
Court Management

### Phase 5
Time Slot Management

### Phase 6
Pricing Management

### Phase 7
Customer Booking

### Phase 8
Availability Display

### Phase 9
Double Booking Protection

### Phase 10
Booking Lookup

### Phase 11
Admin Authentication

### Phase 12
Admin Dashboard

### Phase 13
Booking Management

### Phase 14
Schedule/Calendar View

### Phase 15
Security and Error Handling

---

## Enhancement Phases (16–18)

The following enhancement phases apply to the redesigned booking system using fixed hourly TimeSlots.

> Phases 16, 17, and 18 are complete.

### Phase 16 — Booking Model Redesign to Fixed Hourly TimeSlots

**Objective:** Redesign the booking system to use fixed 1-hour TimeSlots as the atomic unit of booking and availability.

**Tasks:**

- Inspect current Booking implementation
- Inspect current TimeSlot implementation
- Inspect current database schema
- Determine migration strategy for existing bookings
- Redesign Booking model to use:
  - BookingDate (single date)
  - StartTime (e.g., 6:00 PM)
  - EndTime (e.g., 10:00 PM)
  - DurationHours (calculated from StartTime/EndTime)
  - One record per reservation (NOT one per hour)
- Support continuous multi-hour bookings (multi-TimeSlot selection)
- Update availability logic to be TimeSlot-based
- Update pricing calculation to be per-TimeSlot
- Update overlap protection to work with TimeSlot-based availability
- Preserve existing booking data where practical
- Update all affected tests
- Run build
- Run automated tests
- Verify no regressions in existing functionality

**Approval Gate:** Do not implement until existing implementation has been inspected and migration plan is approved.

### Phase 17 — Fixed-TimeSlot Availability UI

**Objective:** Update booking interface to display all 24 hourly TimeSlots with clear availability status.

**Tasks:**

- Display all 24 hourly TimeSlots (12:00 AM through 11:00 PM)
- Show TimeSlot availability status:
  - Available (green)
  - Booked (red)
  - Maintenance/Unavailable (gray)
- Implement continuous TimeSlot selection (no gaps allowed)
- Display selected time range
- Display calculated duration
- Display calculated price
- Prevent non-continuous selection
- Server-side validation of continuous selection
- Responsive desktop layout
- Responsive mobile layout
- Date picker integration
- Court selector integration
- Updated tests for TimeSlot-based availability
- Responsive design tests

### Phase 18 — UI/UX Refinement (Complete)

**Objective:** Improve overall application UI/UX using Bootstrap 5.

**Status:** Complete and merged to `main` (commit `c509b9a`, "BOOKING EXPERIENCE & UX POLISH").

**Completed:**

- ✅ Improve Homepage — hero section, dynamically loaded court cards (with
  availability preview), How It Works, Play Your Way benefits, Find Us, FAQ
  accordion, and final CTA band (`Pages/Index.cshtml`, `Pages/Index.cshtml.cs`)
- ✅ Enhance Navigation — branded responsive navbar with logo, customer links,
  admin dropdown (auth-gated), active-link highlighting, and a modern footer
  (`Pages/Shared/_Layout.cshtml`)
- ✅ Bootstrap Icons integration — icons in navbar, cards, buttons, footer
- ✅ Bootstrap 5 enhancements / design system — brand tokens (CSS variables),
  buttons, cards, forms, badges, hero, footer styles (`wwwroot/css/site.css`)
- ✅ Refine Booking Experience — step indicator, availability legend, past-slot
  disabling, sticky summary, and booking flow polish
- ✅ Improve Schedule/Calendar View — redesigned schedule grid with status legend
- ✅ Error state refinement — consistent alert styling and states
- ✅ Loading state indicators — page loading overlay and per-button spinners

**Dropped items (intentionally out of scope):**

- ~~Full accessibility audit~~ — not pursued; baseline skip link and aria labels kept
- ~~Formal mobile-responsiveness review~~ — not pursued; responsive layouts remain in place

**Notes:**

- Court images are sourced from `wwwroot/images/`.
- Home page availability is a **preview only**; authoritative availability
  remains on the existing Availability/Booking pages.
- No backend, database, or booking/availability logic was changed.

---

## Implementation Guidelines

### Before Each Phase

- Read PROJECT_REQUIREMENTS.md
- Inspect existing implementation
- Understand existing code
- Do not overwrite working functionality unnecessarily
- Follow existing code conventions and patterns

### After Each Phase

1. Build the application
2. Run tests
3. Check for compilation errors
4. Explain what was implemented
5. Provide manual testing checklist
6. Wait for approval before next phase

## Design Principles for Phases 16–30

> These principles apply to all enhancement phases (16–18) and all future
> multi-tenant SaaS phases (19–30). The fixed hourly TimeSlot architecture is
> **preserved** throughout the roadmap.

### Fixed 1-Hour TimeSlots

- 24 standard hourly TimeSlots (12:00 AM through 11:00 PM)
- Each TimeSlot is explicitly Active or Inactive
- Availability is determined per TimeSlot
- Bookings span one or more consecutive TimeSlots
- One Booking record per reservation (not per hour)
- The global 24 TimeSlot definitions remain shared/global across organizations

### Continuous Multi-TimeSlot Bookings

- Customer selects a continuous range of available TimeSlots
- No gaps allowed between selected TimeSlots
- System converts selected TimeSlots to StartTime and EndTime
- Duration calculated from number of selected TimeSlots
- Price calculated for all selected TimeSlots

### Server-Side Authority

- Frontend availability display is informational only
- Server performs final validation immediately before booking creation
- Availability check and booking creation must be atomic
- Price is always recalculated and verified server-side
- No client-provided data (price, status, availability) is trusted

### Preserve Existing Functionality

- All existing Admin pages and features remain active
- Authentication mechanism unchanged
- Booking lookup functionality preserved
- Booking status workflows preserved
- Court and pricing management preserved
- TimeSlot management preserved
- Double booking protection logic preserved
- Error handling and validation preserved
- Responsive UI preserved
* Availability
* Booking form
* Booking confirmation
* Booking lookup
* Admin dashboard
* Admin booking management
* Court management
* Time configuration
* Pricing management
* Forms
* Tables
* Cards
* Buttons
* Status indicators

Preserve existing business logic.

---

## Roadmap (Phases 19–30)

> **Status:** Phases 1–18 are complete. Phase 19 is in progress. Phases 20–30
> are planned (multi-tenant SaaS evolution) and **not yet implemented**.

### Architectural Sequence (Mandatory Order)

The future phases must follow this order. Tenant isolation must come before any
tenant-facing feature (payments, email, subscriptions, platform admin).

1. **Phase 20** must establish the **database foundation**.
2. **Phase 21** must establish **tenant context and server-side isolation**.
3. **Phase 22** must establish **subdomain tenant resolution**.
4. **Phase 23** must establish **organization management/branding**.
5. **Phase 24** must add **manual GCash**.
6. **Phase 25** must add **email/Gmail notifications**.
7. **Phase 26** must add **subscriptions**.
8. **Phase 27** must add **platform administration**.
9. **Phase 28** must perform the **security/tenant isolation audit**.
10. **Phase 29** must prepare for **production**.
11. **Phase 30** is **deployment**.

**Do NOT move GCash, Gmail, subscriptions, or platform administration ahead of
tenant isolation.**

---

## Phase 19 — UI Polish

**Objective:** Polish the existing UI for consistency and usability without
changing business logic.

**Scope:** Presentation-only changes to existing pages and shared components.
No backend, database, booking, availability, or pricing logic changes.

**Main Tasks:**

- Loading states
- Empty states
- Error states
- Confirmation dialogs
- Better validation feedback
- Consistent typography
- Consistent spacing
- Consistent components

**Testing Requirements:**

- Manual review of all affected pages (desktop, tablet, mobile)
- Verify no regressions in booking, availability, lookup, or admin flows
- Build and run existing automated tests

**Approval Gate:** Do not begin Phase 20 until UI polish is reviewed and
approved.

---

## Phase 20 — Multi-Tenant Database Foundation

**Objective:** Establish the multi-tenant database foundation by introducing the
`Organization` tenant abstraction and adding `OrganizationId` to tenant-owned
data, with a migration that moves existing Pikolball data into Organization #1.

**Scope:** Database models and migrations only. Introduce the `Organization` and
`OrganizationMember` models, add `OrganizationId` to tenant-owned tables, and
migrate existing data to Organization #1. Do **not** yet enforce tenant context
in requests (that is Phase 21).

**Main Tasks:**

- Add `Organization` model (Id, Name, Slug, Description, LogoUrl, Phone, Email,
  Address, TimeZone, Currency, Status, CreatedAt, UpdatedAt); `Slug` unique
- Add `OrganizationMember` model (Id, OrganizationId, UserId, Role, CreatedAt)
  with roles: PlatformAdmin, OrganizationOwner, OrganizationAdmin,
  OrganizationStaff
- Add `OrganizationId` to tenant-owned data:
  - Court, Booking, Pricing
  - Court-specific availability/maintenance
  - Organization settings, Payment, Payment settings (models as introduced)
- Keep the global 24 TimeSlot definitions shared/global (preserve fixed hourly
  TimeSlot architecture)
- Migration: create **Organization #1** and assign existing courts, bookings, and
  pricing to it
- Preserve booking history; preserve existing functionality; avoid deleting data
- Backfill `OrganizationId` for existing rows

**Testing Requirements:**

- Verify migration creates Organization #1 and assigns existing data
- Verify all existing bookings, courts, and pricing survive intact
- Verify existing single-organization workflows still function
- Build and run existing automated tests

**Approval Gate:** Do not implement until the model/migration plan is reviewed
and approved. Do not proceed to Phase 21 until the migration is verified.

---

## Phase 21 — Tenant Context & Isolation

**Objective:** Establish the server-side tenant context and enforce tenant
isolation in services and database queries.

**Scope:** Server-side tenant resolution plumbing and query filtering. No
tenant-facing features yet. `OrganizationId` must never be trusted from client
input.

**Main Tasks:**

- Introduce server-side organization/tenant context (resolved per request)
- Enforce `OrganizationId` filtering in services and database queries
- Never accept `OrganizationId` from request bodies, query strings, or hidden
  fields
- Tenants must never see, modify, or access another tenant's:
  - data
  - bookings
  - payment information
  - payment proof files
- Centralize tenant filtering in the service layer

**Testing Requirements:**

- **Cross-tenant automated tests are mandatory:** Tenant A must never see,
  modify, or access Tenant B data, bookings, payments, or payment proof files
- Verify `OrganizationId` is never trusted from client input
- Build and run existing automated tests

**Approval Gate:** Do not proceed to Phase 22 until tenant context and isolation
are verified by tests.

---

## Phase 22 — Subdomain Tenant Resolution

**Objective:** Resolve the current organization from the request subdomain and
populate the organization context.

**Scope:** Request-hostname → subdomain/slug → `Organization` lookup → tenant
context. Depends on Phase 21's tenant context.

**Main Tasks:**

- Map request hostname → subdomain/slug → Organization lookup
- Flow: request hostname → subdomain/slug → Organization → Organization context
  → tenant-aware services → tenant-specific data
- Reserve platform subdomains: `www`, `app`, `admin`, `api`, `mail`, `support`
  (must not resolve to an organization)
- Provide a **safe tenant-resolution strategy for local development** before
  production DNS is configured

**Testing Requirements:**

- Verify subdomain → organization resolution (including reserved subdomains)
- Verify safe behavior when no organization matches
- Verify tenant context correctly drives isolation
- Build and run existing automated tests

**Approval Gate:** Do not proceed to Phase 23 until subdomain resolution and
local-development strategy are reviewed and approved.

---

## Phase 23 — Organization Management & Branding

**Objective:** Allow organizations to manage their profile and branding, and
make the customer-facing booking page use the current organization's information.

**Scope:** Organization profile management (organization-scoped, tenant-isolated)
and branding applied to customer-facing pages.

**Main Tasks:**

- Organization profile management: name, logo, description, contact information,
  address
- Organization-level branding and booking settings
- Customer-facing booking page uses the current organization's information
- Enforce organization-scoped management (admins manage only their own
  organization)

**Testing Requirements:**

- Verify organization info/branding appears on customer-facing booking page
- Verify cross-tenant isolation of organization settings
- Build and run existing automated tests

**Approval Gate:** Do not proceed to Phase 24 until organization management and
branding are reviewed and approved.

---

## Phase 24 — Manual GCash Payment

**Objective:** Add a manual, organization-specific GCash payment workflow with
admin verification. This is **NOT** a GCash API integration.

**Scope:** Manual payment instructions, reference number, proof upload, and
admin verify/reject. Depends on tenant isolation (Phase 21) and organization
settings (Phase 23).

**Main Tasks:**

- Customer flow: select court/date/continuous TimeSlots → enter customer info →
  create booking → display organization-specific GCash instructions → display
  organization-specific GCash QR → pay manually → enter GCash reference number →
  optionally upload payment screenshot/proof
- Admin flow: review → verify or reject payment
- Add `Payment` model: Id, OrganizationId, BookingId, Amount, PaymentMethod,
  PaymentStatus, ReferenceNumber, ProofImageUrl, SubmittedAt, VerifiedAt,
  VerifiedBy
- Payment statuses: Pending, Submitted, Verified, Rejected, Cancelled
- **Customers must never mark their own payment as Verified**
- Add `OrganizationPaymentSettings`: Id, OrganizationId, PaymentMethod,
  AccountName, AccountNumber, QRCodeUrl, Instructions, IsActive
- Tenant-aware payment proof storage
  (`payment-proofs/{organization-slug}/{booking-reference}/...`)

**Testing Requirements:**

- Verify customers cannot self-verify payments
- Verify admin verify/reject transitions
- Verify payment proof storage is tenant-aware (no cross-tenant access)
- Verify organization-specific instructions/QR
- Build and run existing automated tests

**Approval Gate:** Do not proceed to Phase 25 until the manual GCash flow and
its isolation are reviewed and approved.

---

## Phase 25 — Email / Gmail Notifications

**Objective:** Add email notifications as a notification channel (not a
database).

**Scope:** Server-side email service abstraction with organization-specific
content. Depends on tenant context and organization settings.

**Main Tasks:**

- Server-side email service abstraction
- Customer notifications (may include): booking received, payment instructions,
  payment submitted, payment verified, payment rejected, booking cancelled
- Organization notifications (may include): new booking, payment submitted,
  booking cancellation
- Emails must use organization-specific information
- Do **not** require every organization owner to connect a personal Gmail
  initially
- Optional per-organization Gmail OAuth considered later

**Testing Requirements:**

- Verify notifications trigger on the correct events
- Verify emails use the correct organization's information
- Build and run existing automated tests

**Approval Gate:** Do not proceed to Phase 26 until the email service and
notification triggers are reviewed and approved.

---

## Phase 26 — Subscription Management

**Objective:** Add SaaS subscription plans and subscriptions with manual
activation. No online billing at this stage.

**Scope:** Subscription models and manual activation/status management.

**Main Tasks:**

- Add `SubscriptionPlan`: Id, Name, Price, BillingPeriod, MaxCourts, MaxStaff,
  Features, IsActive
- Add `Subscription` model: Id, OrganizationId, PlanId, Status, StartDate,
  EndDate, TrialEndDate
- Subscription statuses: Trial, Active, Expired, Suspended, Cancelled
- Support **manual activation** (no online billing)

**Testing Requirements:**

- Verify plan assignment and status transitions
- Verify manual activation works
- Build and run existing automated tests

**Approval Gate:** Do not proceed to Phase 27 until subscription management is
reviewed and approved.

---

## Phase 27 — Platform Administration

**Objective:** Add platform-level administration for managing organizations.

**Scope:** PlatformAdmin capabilities; organization admins remain scoped to their
own organization.

**Main Tasks:**

- PlatformAdmin may: create organizations, view organizations, activate/
  deactivate organizations, manage organization membership, assign subscription
  plans, view subscription status, view high-level platform information
- Organization admins must only manage their own organization

**Testing Requirements:**

- Verify PlatformAdmin capabilities
- Verify organization admins cannot manage other organizations
- Build and run existing automated tests

**Approval Gate:** Do not proceed to Phase 28 until platform administration is
reviewed and approved.

---

## Phase 28 — Security & Tenant Isolation Audit

**Objective:** Perform a full security and tenant isolation audit of the
multi-tenant platform.

**Scope:** Audit, not new features. Verify isolation across services, queries,
storage, and admin surfaces.

**Main Tasks:**

- Audit tenant isolation across services and database queries
- Verify no cross-tenant data access (bookings, payments, payment proof files)
- Verify `OrganizationId` is never trusted from client input
- Verify tenant filtering is enforced centrally
- Review authentication/authorization and role scoping

**Testing Requirements:**

- Comprehensive cross-tenant testing (see Phase 21)
- Security-focused regression tests
- Build and run full automated test suite

**Approval Gate:** Do not proceed to Phase 29 until the audit passes and
findings are resolved.

---

## Phase 29 — Final UI/UX & Production Readiness

**Objective:** Prepare the multi-tenant platform for production.

**Scope:** Organization-aware UI/UX polish and production readiness checks.

**Main Tasks:**

- Polish organization-aware UI/UX
- Production readiness checks (configuration, secrets, logging, error handling)
- Verify all phases 20–28 requirements are satisfied

**Testing Requirements:**

- Full regression pass (desktop, tablet, mobile)
- Full automated test suite
- Production configuration verification

**Approval Gate:** Do not proceed to Phase 30 until production readiness is
reviewed and approved.

---

## Phase 30 — Deployment

**Objective:** Deploy the multi-tenant platform to production.

**Scope:** Production deployment of the multi-tenant application.

**Main Tasks:**

- Configure production DNS, tenant subdomains, and reserved platform subdomains
- Provision production database and apply migrations
- Configure secrets and environment variables
- Deploy and verify organization resolution, isolation, and core flows

**Testing Requirements:**

- Post-deployment smoke tests (booking, lookup, admin, organization resolution)
- Verify tenant isolation in production
- Monitor logs for errors

**Approval Gate:** Final sign-off after successful deployment verification.

