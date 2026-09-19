# PICKLEBALL BOOKING SYSTEM - UPDATED REQUIREMENTS

## PROJECT OVERVIEW

A simple, maintainable, responsive web-based Pickleball Court Booking System using:

- C# and ASP.NET Core
- Razor Pages
- Entity Framework Core
- PostgreSQL / Supabase
- Bootstrap
- ASP.NET Core Identity

**Target Cost:** ₱0 (free/open-source technologies only)

**Architecture:** Simple monolithic Razor Pages application with service layer

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

### Evolution to Multi-Tenant SaaS (Future Phases)

The system is evolving from a **single-organization booking application** into a
**multi-tenant SaaS platform**. Multiple independent pickleball court
organizations will be able to use the same application, each isolated from the
others.

Example tenant subdomains:

```
pikolball.example.com
acepickleball.example.com
smashzone.example.com
```

The **subdomain identifies the organization** (the tenant).

- **Tenant abstraction:** `Organization` (the primary tenant abstraction).
- **Do NOT use `CourtOwner`** as the primary tenant abstraction.
- **Initial topology (this stage):**
  - ONE ASP.NET Core application
  - ONE PostgreSQL / Supabase database
  - `OrganizationId` used for tenant isolation
- **Do NOT introduce separate databases per tenant at this stage.**

This evolution is planned across **Phases 20–30** (see Section 22). Until those
phases are implemented, the system continues to operate as the approved
single-organization application described in Sections 1–21, and all existing
functionality (booking, availability, pricing, admin, security, testing) remains
in effect.

---

# 1. TECHNOLOGY STACK

### Use:
- C#
- ASP.NET Core (.NET 10)
- Razor Pages
- Entity Framework Core
- PostgreSQL
- Supabase (free tier)
- Bootstrap 5
- Bootstrap Icons
- ASP.NET Core Identity
- Git / GitHub

### Do NOT Use:
- React, Angular, Vue, Next.js
- Node.js backend
- TypeScript
- Microservices
- Redis, Kubernetes, Docker (unless required)
- Paid APIs or SaaS services
- Payment gateways (GCash, PayMongo, Xendit, Stripe, PayPal)

### Do NOT Introduce (Multi-Tenant SaaS Constraints)

The following must **not** be introduced even as the platform evolves to
multi-tenant SaaS (see Section 22, Phases 20–30):

- React
- Angular
- Vue
- Next.js
- Node.js backend
- TypeScript
- Microservices
- Redis
- Kubernetes
- Separate database per tenant
- Custom domains for organizations as a requirement
- Automatic GCash payment gateway
- Online subscription billing at this stage

### Simple Monolithic Architecture

No over-engineering. Single application. Service layer for business logic.

---

# 2. VERSION 1 SCOPE

### CUSTOMER FEATURES
- Home page
- Modern booking/calendar interface
- Date selection
- Court selection and availability
- Fixed hourly TimeSlot selection
- Multi-TimeSlot continuous booking
- Price display
- Customer information (name, phone, email)
- Booking submission
- Booking reference
- Booking status
- Booking lookup (no login required)

### ADMIN FEATURES
- Secure login
- Dashboard
- Court management
- TimeSlot management
- Pricing management
- Booking management
- Booking confirmation workflow
- Booking cancellation
- Booking completion
- Schedule/calendar view

---

# 3. PAYMENT

**NO PAYMENT FUNCTIONALITY in Version 1.**

Do NOT implement:
- GCash API
- PayMongo
- Xendit
- Stripe
- PayPal
- Credit cards
- Payment gateway
- Automatic payment verification
- SMS payment verification

The system must work completely without online payment.

Payment may be added in a future version.

> **Future note (Phase 24 — Manual GCash Payment):** A **manual** GCash payment
> workflow (customer pays manually, submits a reference number / proof, and an
> organization admin verifies it) is planned. This is explicitly **NOT** a GCash
> API integration and does **NOT** change the Version 1 rule above. See
> Section 25 for details.

---

# 4. COURTS

### Court Model

Fields:
- Id
- Name
- Description
- Status (Active | Inactive)
- CreatedAt
- UpdatedAt

> **Future (Phase 20 — Multi-Tenant Database Foundation):** Court becomes
> **tenant-owned data** and will also carry an `OrganizationId` field to isolate
> courts by organization. See Section 24 (Tenant-Owned Data).

### Dynamic Courts

Courts must NOT be hard-coded.

Courts must be stored in the database.

Administrator must be able to:
- Add court
- Edit court
- Activate court
- Deactivate court

New courts automatically appear on customer booking page (no code changes required).

### Data Preservation

Do not permanently delete courts that have booking history.

Prefer deactivation.

---

# 5. FIXED HOURLY TIMESLOTS

## TimeSlot Architecture

The system uses **24 standard fixed hourly TimeSlots** as the atomic unit of booking and availability:

```
12:00 AM – 1:00 AM
1:00 AM – 2:00 AM
2:00 AM – 3:00 AM
3:00 AM – 4:00 AM
4:00 AM – 5:00 AM
5:00 AM – 6:00 AM
6:00 AM – 7:00 AM
7:00 AM – 8:00 AM
8:00 AM – 9:00 AM
9:00 AM – 10:00 AM
10:00 AM – 11:00 AM
11:00 AM – 12:00 PM
12:00 PM – 1:00 PM
1:00 PM – 2:00 PM
2:00 PM – 3:00 PM
3:00 PM – 4:00 PM
4:00 PM – 5:00 PM
5:00 PM – 6:00 PM
6:00 PM – 7:00 PM
7:00 PM – 8:00 PM
8:00 PM – 9:00 PM
9:00 PM – 10:00 PM
10:00 PM – 11:00 PM
11:00 PM – 12:00 AM
```

### Key Characteristics

Each TimeSlot:
- Represents exactly 1 hour
- Can be individually available or booked
- Cannot be subdivided
- Is shared across all courts
- Has a status: Active or Inactive

### TimeSlot Status

- **Active** — Available for booking
- **Inactive** — Not available for booking (maintenance, closed hours, etc.)

### TimeSlot Management

Administrator must be able to:
- View all 24 hourly TimeSlots
- Activate/deactivate individual TimeSlots
- Configure which hours are bookable
- Support overnight hours

TimeSlots must be dynamic and configurable.

Do not hard-code TimeSlots into customer booking pages.

> **Future (Multi-Tenant SaaS):** The global 24 TimeSlot definitions may remain
> **shared/global** across all organizations (they are the atomic hourly unit and
> are not necessarily tenant-owned). Tenant-specific availability/maintenance is
> what becomes tenant-scoped. See Section 24 (Tenant-Owned Data).

### Continuous Bookings Across TimeSlots

Customers may reserve **multiple consecutive hourly TimeSlots** in a **single Booking record**.

**CRITICAL:** Do NOT create one Booking record per hour.

Example:
- Customer selects TimeSlots 6:00 PM, 7:00 PM, 8:00 PM, 9:00 PM (4 consecutive hours)
- Result: Single Booking from 6:00 PM–10:00 PM
- Duration: 4 hours
- Price: Calculated for 4 hours

### Availability Display

For a customer-selected Court and Date:

- Display all 24 hourly TimeSlots
- For each TimeSlot, show:
  - **Available** — TimeSlot is active AND no overlapping booking exists
  - **Booked** — Active booking occupies this TimeSlot
  - **Maintenance/Unavailable** — TimeSlot is inactive
- Allow customer to select a continuous range of available TimeSlots
- Prevent selection of non-continuous ranges (gaps not allowed)

---

# 6. BOOKINGS

## Booking Model

One Booking record represents an entire reservation.

### Booking Fields

- **Id** — Unique identifier
- **BookingReference** — Unique user-facing reference (e.g., PB-20260920-0001)
- **CustomerName** — Full name (required)
- **CustomerPhone** — Mobile number (required)
- **CustomerEmail** — Email address (required)
- **CourtId** — Foreign key to Courts table
- **BookingDate** — Single date for the booking (DateOnly, cannot be in past)
- **StartTime** — Booking beginning time (TimeSpan, e.g., 6:00 PM)
- **EndTime** — Booking ending time (TimeSpan, e.g., 10:00 PM)
- **DurationHours** — Hours reserved, calculated from EndTime - StartTime (decimal)
- **Price** — Server-calculated total price for entire booking
- **BookingStatus** — Pending | Confirmed | Cancelled | Completed
- **CreatedAt** — Timestamp
- **UpdatedAt** — Timestamp

> **Future (Phase 20 — Multi-Tenant Database Foundation):** Booking becomes
> **tenant-owned data** and will also carry an `OrganizationId` field so that
> each organization's bookings are isolated. The **one-Booking-per-reservation**
> model is preserved unchanged. See Section 24 (Tenant-Owned Data).

### Single Booking Per Reservation

Example:
- Customer selects TimeSlots 6:00 PM, 7:00 PM, 8:00 PM, 9:00 PM
- Create ONE Booking record:
  - StartTime: 6:00 PM
  - EndTime: 10:00 PM
  - DurationHours: 4
  - Price: (calculated for 4 hours)

Do NOT create four separate Booking records.

### Duration Calculation

DurationHours is calculated server-side:

```
DurationHours = (EndTime - StartTime).TotalHours
```

Example:
- StartTime: 6:00 PM
- EndTime: 10:00 PM
- DurationHours: 4.0

### Booking Status Workflow

1. **Pending** — Initial status when customer submits booking
2. **Confirmed** — Admin has confirmed the booking
3. **Cancelled** — Booking cancelled (releases TimeSlots)
4. **Completed** — Booking completed (after booking date passes)

### Time Validation

The system must reject:
- EndTime earlier than StartTime
- EndTime equal to StartTime
- Booking outside active TimeSlots
- Bookings with gaps between selected TimeSlots
- Invalid time ranges

---

# 7. BOOKING FLOW

## Customer Booking Steps

1. Open website
2. Open booking/calendar page
3. Select booking date
4. View list of active courts
5. Select court
6. View all 24 hourly TimeSlots for selected court and date
7. See TimeSlot availability status (Available, Booked, Maintenance/Unavailable)
8. Select continuous range of available TimeSlots
9. System validates selection:
   - Must be continuous (no gaps)
   - All selected TimeSlots must be available
10. System calculates:
	- Duration (number of selected hours)
	- StartTime (first selected TimeSlot)
	- EndTime (last selected TimeSlot start + 1 hour)
	- Price (based on duration, period, weekday/weekend)
11. Customer reviews booking:
	- Court name
	- Date
	- Time range (e.g., 18:00–22:00)
	- Duration
	- Total price
12. Customer enters:
	- Full name
	- Mobile number
	- Email address
13. Customer submits booking
14. System performs final server-side validation:
	- Validate all customer input
	- Re-check availability
	- No overlapping bookings exist
	- Court is active
	- All TimeSlots are active
	- Re-calculate price and verify
	- Booking reference is unique
15. System creates ONE Booking record
16. System generates unique booking reference
17. System displays booking confirmation:
	- Booking reference
	- All booking details
	- Booking status (Pending)
	- Next steps

## Example

- **Date:** September 20, 2026
- **Court:** Court 2
- **TimeSlots:** 6:00 PM, 7:00 PM, 8:00 PM, 9:00 PM
- **StartTime:** 6:00 PM
- **EndTime:** 10:00 PM
- **Duration:** 4 hours
- **Price:** Calculated from pricing rules
- **Booking Reference:** PB-20260920-0001

## Single Booking Per Reservation

One booking record for the entire continuous period.

## No Customer Registration Required

Version 1 does not require customer registration or login.

Customers book anonymously using name, phone, and email.

---

# 8. AVAILABILITY

## Availability Per TimeSlot

Availability is determined for each hourly TimeSlot based on:

- **Court** — The selected court
- **BookingDate** — The selected date
- **TimeSlot** — One of the 24 hourly slots

For each TimeSlot, the system determines:

1. Is the TimeSlot **Active** or **Inactive**?
2. Does an **overlapping booking** occupy this TimeSlot?
   - Active bookings that include this hour block it
   - Cancelled bookings do NOT block it
   - Pending bookings DO block it (tentatively reserved)

### TimeSlot States

- **Available** — TimeSlot is active AND no overlapping booking exists
- **Booked** — TimeSlot is active BUT overlapping booking exists
- **Maintenance/Unavailable** — TimeSlot is inactive

### Multi-TimeSlot Booking Availability

To check availability for a range of TimeSlots (e.g., 6:00 PM–10:00 PM):

1. Check each individual TimeSlot in the range
2. All TimeSlots must be Available
3. All TimeSlots must be continuous (no gaps)
4. If any TimeSlot is Booked or Unavailable, the range is NOT available

### Back-to-Back Bookings Allowed

Example:
- **Existing booking:** 6:00 PM–8:00 PM
- **Requested booking:** 8:00 PM–10:00 PM
- **Result:** ALLOWED

The end time of one booking may equal the start time of another booking.

### Cancelled Bookings Release TimeSlots

When a booking is cancelled:
- Status changes to Cancelled
- All reserved TimeSlots are released
- Those TimeSlots become available immediately
- Future bookings can now use those slots
- Cancelled booking remains in system for record-keeping

### Server-Side Validation

The server must perform final availability check immediately before creating booking.

Frontend availability display is never authoritative.

System must protect against two customers booking overlapping periods simultaneously.

---

# 9. DOUBLE BOOKING PROTECTION

This is a critical business rule.

The system must never allow two active bookings for the same:
- Court
- BookingDate
- Overlapping time period

### Overlap Detection

A requested booking overlaps an existing active booking when:

```
ExistingStartTime < RequestedEndTime
AND
ExistingEndTime > RequestedStartTime
```

### Overlap Examples

#### Overlap — Request Contained Within Existing
- **Existing:** 6:00 PM–10:00 PM
- **Requested:** 7:00 PM–8:00 PM
- **Result:** REJECTED

#### Overlap — Request Extends Beyond Existing
- **Existing:** 6:00 PM–8:00 PM
- **Requested:** 7:00 PM–10:00 PM
- **Result:** REJECTED

#### Back-to-Back — NOT an Overlap
- **Existing:** 6:00 PM–8:00 PM
- **Requested:** 8:00 PM–10:00 PM
- **Result:** ALLOWED (no overlap)

#### Before Existing — NOT an Overlap
- **Existing:** 6:00 PM–8:00 PM
- **Requested:** 4:00 PM–6:00 PM
- **Result:** ALLOWED

#### Exact Match — Overlap
- **Existing:** 6:00 PM–10:00 PM
- **Requested:** 6:00 PM–10:00 PM
- **Result:** REJECTED

### Protection Requirements

Before creating a booking:

1. Check availability for all requested TimeSlots
2. Validate customer input (name, phone, email)
3. Validate the requested time range
4. Validate the court is active
5. Validate all TimeSlots are active
6. Perform final server-side overlap check
7. Use database constraints (unique constraint on Court + Date + overlapping time ranges)
8. Use database transactions to prevent race conditions
9. Re-check availability one final time
10. Reject if overlapping active booking exists
11. Return friendly error message if rejected

### Concurrent Booking Protection

Implementation must handle two customers attempting to book overlapping periods at approximately the same time.

Protection mechanisms:
- **Database-Level Constraint** — Prevent duplicate Court + Date + TimeSlot reservations
- **Transaction Isolation** — Use appropriate transaction isolation level
- **Server-Side Check** — Re-check immediately before booking creation
- **Atomic Operation** — Availability check and booking creation must be atomic

---

# 10. BOOKING REFERENCE

Generate unique booking reference.

Example: PB-20260920-0001

The reference must be:
- Unique
- User-friendly
- Displayed immediately after booking creation

Used for booking lookup.

---

# 11. BOOKING LOOKUP

Customers do not need an account.

Allow booking lookup using:
- Booking Reference
- Customer Mobile Number or Email

Security:
- Only return the matching booking
- Do not expose other customers' bookings
- Require both reference and mobile/email to view booking

---

# 12. COURT-SPECIFIC MAINTENANCE

The system must support court-specific maintenance/availability configuration.

Administrator can mark specific courts as:
- Temporarily closed for maintenance
- Unavailable for specific dates
- Scheduled for maintenance during specific time windows

Implementation approaches:

1. **Inactive TimeSlots per Court** — Create court-specific inactive status for each TimeSlot on specific dates
2. **Maintenance Records** — Create separate Maintenance records that block booking availability
3. **Court Status** — Set court status to Inactive (affects all dates until re-activated)

### Global TimeSlot Deactivation

Individual TimeSlots can be deactivated globally, making them unavailable for all courts on all dates.

Example: If midnight (11:00 PM–12:00 AM) is not bookable, deactivate that TimeSlot globally.

> **Future (Multi-Tenant SaaS):** Court-specific availability / maintenance is
> **tenant-owned data** and must be isolated by `OrganizationId`. The global
> TimeSlot definitions themselves may remain shared. See Section 24 (Tenant-Owned
> Data).

---

# 13. PRICING

## Multi-Hour Booking Pricing

Pricing supports bookings spanning multiple hourly TimeSlots.

Price is calculated from:
- **BookingDate** — The date
- **StartTime** — Booking beginning
- **EndTime** — Booking ending
- **DurationHours** — Number of TimeSlots (calculated)
- **DayType** — Weekday or weekend
- **Applicable pricing configuration**

## Pricing Per TimeSlot

Pricing is calculated per hourly TimeSlot.

Example:
- **Booking:** 6:00 PM–10:00 PM (4 hours)
- **Rate:** ₱400/hour
- **Price:** 4 × ₱400 = ₱1,600

## Pricing Across Multiple Periods

If a booking crosses multiple pricing periods, calculate applicable rate for each portion separately.

Example:
- **Booking:** 4:00 PM–6:00 PM
- **4:00 PM–5:00 PM:** ₱300/hour (Rate 1)
- **5:00 PM–6:00 PM:** ₱400/hour (Rate 2)
- **Calculation:**
  - 1 × ₱300 = ₱300
  - 1 × ₱400 = ₱400
  - **Total:** ₱700

## Weekday and Weekend Pricing

Support different pricing for weekdays and weekends.

Configuration:
- **Weekday (Monday–Friday)** — One set of hourly rates
- **Weekend (Saturday–Sunday)** — One set of hourly rates

Each period can have different rates by hour.

Example:
- **Weekday 8:00 AM–5:00 PM:** ₱300/hour
- **Weekday 5:00 PM–10:00 PM:** ₱400/hour
- **Weekend 8:00 AM–10:00 PM:** ₱400/hour

## Overnight Pricing Ranges

Support pricing that crosses midnight.

Example:
- **6:00 PM–2:00 AM (next day):** ₱500/hour
- Booking from 6:00 PM Monday to 2:00 AM Tuesday uses overnight rate for 8 hours

## Server-Side Price Calculation

Price MUST be calculated server-side.

Customer must NOT be able to:
- Submit arbitrary price
- Override calculated price
- Manipulate pricing

Server must:
- Calculate price based on configured rules
- Re-calculate immediately before booking creation
- Verify final price matches calculated price
- Store calculated price in booking record

## Price Configuration Management

Administrator must be able to:
- Add pricing configuration
- Edit pricing configuration
- Activate/deactivate pricing configuration
- View historical pricing (for reconciliation)
- Support multiple pricing tiers by day type, period, and rate

Price configuration must be dynamic.

Do not hard-code prices into customer booking pages.

> **Future (Phase 20 — Multi-Tenant Database Foundation):** Pricing becomes
> **tenant-owned data** and will carry an `OrganizationId` field so that each
> organization's pricing rules are isolated. See Section 24 (Tenant-Owned Data).

---

# 14. ADMIN FEATURES

### Admin Authentication

Use ASP.NET Core Identity.

Protect all Admin pages.

Only authorized administrators can access:
- /Admin
- /Admin/Bookings
- /Admin/Courts
- /Admin/TimeSlots
- /Admin/Pricing

Secrets:
- Do not store passwords manually
- Do not place credentials in source code
- Use secure configuration (User Secrets development, environment variables production)

> **Future (Phase 27 — Platform Administration):** Administration will be split
> into **PlatformAdmin** (manages all organizations) and **Organization
> admins/staff** (manage only their own organization). Organization admins must
> only ever manage their own organization. See Sections 23 and 26.

### Admin Dashboard

Display:
- Today's Bookings
- Pending Bookings
- Confirmed Bookings
- Completed Bookings
- Active Courts

Provide links to:
- Bookings
- Courts
- TimeSlots
- Pricing
- Schedule

Keep dashboard simple.

### Admin Booking Management

Display fields:
- Booking Reference
- Customer
- Court
- Date
- Time
- Price
- Status
- Created Date

Allow filtering by:
- Date
- Court
- Status
- Customer

Actions:
- View booking details
- Confirm booking
- Cancel booking
- Complete booking

### Admin Court Management

Allow:
- Add court
- Edit court
- Activate court
- Deactivate court
- View all courts (active and inactive)

### Admin TimeSlot Management

Allow:
- View all 24 hourly TimeSlots
- Activate/deactivate individual TimeSlots
- Configure which hours are bookable

### Admin Pricing Management

Allow:
- Add pricing configuration
- Edit pricing configuration
- Activate/deactivate pricing configuration
- View historical pricing
- Support weekday/weekend differentiation
- Support time-period-based rates
- Support overnight ranges

---

# 15. VALIDATION

Server-side validation is mandatory.

Validate:
- Customer name (required)
- Mobile number (required)
- Valid email format
- Booking date (required, not in past)
- StartTime and EndTime (required, valid)
- Court (required, must be active)
- TimeSlots (required, must be active)
- Price (calculated server-side, never trusted from client)
- Booking reference (must be unique)
- Time range (continuous, no gaps)
- No overlapping bookings for same court/date

Never trust client-provided:
- Price
- Booking status
- Court availability
- Duration
- TimeSlot data

---

# 16. ERROR HANDLING

### Customer-Friendly Error Messages

Return friendly, non-technical messages:
- "Sorry, this time slot is no longer available. Please select another time."
- "Please enter a valid email address."
- "This court is not available for that date."
- "Booking date cannot be in the past."
- "Please select a continuous time range with no gaps."

Do NOT expose:
- Stack traces
- Database errors
- SQL queries
- Connection strings
- Internal exceptions
- Sensitive system information

### Logging

Use ASP.NET Core logging for technical errors:
- Log all exceptions
- Log booking creation failures
- Log availability check failures
- Log validation errors
- Do not log sensitive customer data
- Do not log prices unless necessary for reconciliation
- Implement log retention and cleanup

---

# 17. SECURITY

### Authentication & Authorization

- Authentication (login)
- Authorization (role-based access control)
- Admin pages protected
- Customer pages accessible

### Data Protection

- Anti-forgery protection (CSRF tokens)
- Server-side validation (all inputs)
- Secure configuration (User Secrets, environment variables)
- Proper database access (parameterized queries)
- Protection against unauthorized booking lookup
- HTTPS (if deployed to production)

### Secrets & Configuration

Avoid:
- Hard-coded connection strings
- Hard-coded credentials
- API keys in source code
- Sensitive data in logs
- Stack traces exposed to customers

Use:
- User Secrets (development)
- Environment variables (production)
- Secure configuration providers
- Safe exception logging

---

# 18. VERSION 1 ACCEPTANCE CRITERIA

### CUSTOMER EXPERIENCE

✓ View website
✓ Open modern booking/calendar interface
✓ Navigate to select booking date
✓ View active courts
✓ View court availability for selected date
✓ Select court
✓ See all 24 hourly TimeSlots
✓ See TimeSlot status (Available, Booked, Maintenance/Unavailable)
✓ Select continuous range of available TimeSlots
✓ See calculated duration (number of hours)
✓ See calculated price
✓ Cannot select invalid time ranges (gaps, reversed times)
✓ Cannot select unavailable periods
✓ Cannot overlap existing active bookings
✓ Can book back-to-back with existing bookings
✓ Enter name
✓ Enter mobile number
✓ Enter email
✓ Submit booking
✓ Receive booking reference
✓ Look up booking (by reference + mobile/email)
✓ Cannot book past dates
✓ Cannot manipulate server-calculated price
✓ Booking works on mobile devices
✓ Responsive design on all screen sizes

### ADMIN EXPERIENCE

✓ Login with account
✓ Access Admin dashboard
✓ Manage courts (add, edit, activate, deactivate)
✓ Manage TimeSlots (activate, deactivate)
✓ Manage pricing (add, edit, configure rates)
✓ View all bookings
✓ Search/filter bookings (date, court, status, customer)
✓ Confirm booking
✓ Cancel booking
✓ Complete booking
✓ View schedule/calendar
✓ Protected access (only admins can enter)

### SYSTEM REQUIREMENTS

✓ C# and ASP.NET Core (NET 10)
✓ Razor Pages
✓ Entity Framework Core
✓ PostgreSQL
✓ Supabase
✓ Bootstrap responsive design
✓ Secure authentication
✓ Fixed hourly TimeSlots (24 slots)
✓ Multi-TimeSlot continuous bookings
✓ Dynamic courts
✓ Dynamic TimeSlots
✓ Configurable pricing
✓ Weekday/Weekend pricing
✓ Double-booking protection
✓ Concurrent booking protection
✓ Automated tests
✓ No payment system
✓ No paid service dependencies

---

# 19. PRESERVED EXISTING FUNCTIONALITY

All existing functionality remains active:

✓ All existing Admin pages and features
✓ Authentication mechanism
✓ Booking lookup functionality
✓ Booking status workflows
✓ Court management
✓ Pricing management
✓ TimeSlot management
✓ Double booking protection
✓ Error handling and validation
✓ Responsive UI

Do not delete existing functionality.

Do not break existing workflows.

---

# 20. TESTING

Automated tests must cover:

1. ✓ Valid multi-TimeSlot continuous booking creation
2. ✓ StartTime stored correctly
3. ✓ EndTime stored correctly
4. ✓ DurationHours calculated correctly
5. ✓ EndTime before StartTime rejected
6. ✓ EndTime equal to StartTime rejected
7. ✓ Booking outside active TimeSlots rejected
8. ✓ Overlapping booking rejected
9. ✓ Back-to-back booking allowed
10. ✓ Request completely overlapping existing booking rejected
11. ✓ Request containing existing booking but overlapping rejected
12. ✓ Partial overlap at beginning rejected
13. ✓ Partial overlap at end rejected
14. ✓ Cancelled booking does not block availability
15. ✓ Correct price for multi-hour booking
16. ✓ Correct price when crossing pricing periods/day types
17. ✓ Client-provided price cannot override server-calculated price
18. ✓ Inactive court rejected
19. ✓ Inactive TimeSlot rejected
20. ✓ Past booking date rejected
21. ✓ Two concurrent booking attempts cannot both succeed for overlapping periods
22. ✓ Booking reference is unique
23. ✓ Booking lookup works (by reference + mobile/email)
24. ✓ Existing admin booking functionality continues to work
25. ✓ Existing booking status transitions continue to work
26. ✓ Server-side validation catches all invalid inputs
27. ✓ Friendly error messages returned to customer
28. ✓ TimeSlot availability display correct
29. ✓ Continuous TimeSlot selection enforced
30. ✓ Non-continuous selection prevented

> **Future (Multi-Tenant SaaS — Phase 21 / Phase 28):** **Cross-tenant automated
> tests are mandatory.** They must verify that Tenant A can never see, modify,
> or access Tenant B's data, bookings, payment information, or payment proof
> files, and that `OrganizationId` is never trusted from client input. See
> Section 29 (Tenant Isolation).

---

# 21. SAMPLE DATA

Development seed data:

### Courts

- Court 1
- Court 2
- Court 3

### TimeSlots (All 24 hourly slots)

```
12:00 AM–1:00 AM
1:00 AM–2:00 AM
2:00 AM–3:00 AM
3:00 AM–4:00 AM
4:00 AM–5:00 AM
5:00 AM–6:00 AM
6:00 AM–7:00 AM
7:00 AM–8:00 AM
8:00 AM–9:00 AM
9:00 AM–10:00 AM
10:00 AM–11:00 AM
11:00 AM–12:00 PM
12:00 PM–1:00 PM
1:00 PM–2:00 PM
2:00 PM–3:00 PM
3:00 PM–4:00 PM
4:00 PM–5:00 PM
5:00 PM–6:00 PM
6:00 PM–7:00 PM
7:00 PM–8:00 PM
8:00 PM–9:00 PM
9:00 PM–10:00 PM
10:00 PM–11:00 PM
11:00 PM–12:00 AM
```

All TimeSlots initially Active.

### Pricing

Sample pricing configuration (editable through Admin):

- **Weekday (Mon–Fri) 8:00 AM–5:00 PM:** ₱300/hour
- **Weekday (Mon–Fri) 5:00 PM–10:00 PM:** ₱400/hour
- **Weekend (Sat–Sun) 8:00 AM–10:00 PM:** ₱400/hour
- **Overnight (10:00 PM–8:00 AM):** ₱250/hour

These are development defaults only.

---

# 22. DEVELOPMENT PHASES

### COMPLETED (Phases 1–15)

- [x] Environment and project setup
- [x] PostgreSQL / Supabase configuration
- [x] Database models and migrations
- [x] Court management
- [x] TimeSlot management
- [x] Pricing management
- [x] Customer booking pages
- [x] Availability display
- [x] Double booking protection
- [x] Booking lookup
- [x] Admin authentication
- [x] Admin dashboard
- [x] Booking management
- [x] Schedule/calendar view
- [x] Security and error handling

### NEW PHASES (16–18)

**Phase 16** — Booking Model Redesign to Fixed Hourly TimeSlots
- Inspect existing Booking implementation
- Redesign to use fixed 1-hour TimeSlots
- Support multi-TimeSlot continuous bookings
- Update pricing for per-TimeSlot calculation
- Update availability logic for TimeSlot-based determination
- Update overlap protection
- Migrate existing data
- Update affected tests
- Verify build

**Phase 17** — Fixed-TimeSlot Availability UI
- Display all 24 hourly TimeSlots
- Show availability per TimeSlot
- Allow customer to select continuous range
- Display duration and price
- Responsive design (desktop and mobile)
- Updated tests

**Phase 18** — UI/UX Refinement (Complete)
- ✅ Bootstrap 5 improvements (design system: tokens, buttons, cards, forms, badges)
- ✅ Modern responsive layout (hero homepage, branded navbar, footer)
- ✅ Bootstrap Icons integration
- ✅ Booking experience polish (step indicator, legend, past-slot disabling, sticky summary)
- ✅ Schedule/calendar view redesign
- ✅ Error state refinement
- ✅ Loading state indicators

**Phase 19** — UI Polish (In Progress)
- ✅ Toast notifications (inline alerts enhanced into floating toasts)
- ✅ Confirmation dialogs (reusable modal for destructive actions)

### FUTURE PHASES (20–30) — Multi-Tenant SaaS Evolution

> These phases describe the approved future architecture. They are **planned**
> and **not yet implemented**. See Sections 24–35 for the underlying
> requirements.

**Phase 20** — Multi-Tenant Database Foundation
- Introduce the `Organization` and `OrganizationMember` models
- Add `OrganizationId` to tenant-owned data (Court, Booking, Pricing,
  maintenance, settings, payment, payment settings)
- Migration: create Organization #1 and assign existing data to it
- Preserve booking history and existing functionality

**Phase 21** — Tenant Context & Isolation
- Server-side tenant resolution and organization context
- Enforce `OrganizationId` filtering in services/database queries
- Never trust `OrganizationId` from client input
- Cross-tenant automated tests

**Phase 22** — Subdomain Tenant Resolution
- Map request hostname → subdomain/slug → Organization
- Reserve platform subdomains (www, app, admin, api, mail, support)
- Safe local development tenant-resolution strategy

**Phase 23** — Organization Management & Branding
- Organization profile management (name, logo, description, contact, address)
- Organization-level branding and booking settings
- Customer-facing booking page uses the current organization's information

**Phase 24** — Manual GCash Payment
- Organization-specific GCash instructions and QR
- Customer reference number + payment proof upload
- Admin verify/reject workflow
- `OrganizationPaymentSettings`
- Tenant-aware payment proof storage
- (Not a GCash API integration)

**Phase 25** — Email / Gmail Notifications
- Server-side email service abstraction
- Customer and organization notifications
- Organization-specific email content
- Optional per-organization Gmail OAuth considered later

**Phase 26** — Subscription Management
- `SubscriptionPlan` and `Subscription` models
- Trial / Active / Expired / Suspended / Cancelled statuses
- Manual activation (no online billing yet)

**Phase 27** — Platform Administration
- PlatformAdmin capabilities (create/view/activate organizations, membership,
  subscription plans)
- Organization admins limited to their own organization

**Phase 28** — Security & Tenant Isolation Audit
- Audit tenant isolation across services and queries
- Verify no cross-tenant data access (bookings, payments, proof files)
- Verify `OrganizationId` is never trusted from client input

**Phase 29** — Final UI/UX & Production Readiness
- Polish organization-aware UI/UX
- Production readiness checks

**Phase 30** — Deployment
- Production deployment of the multi-tenant platform

---

# 23. KEY DESIGN DECISIONS

## Fixed Hourly TimeSlots

**Why?**

- **Deterministic availability:** Each hour is explicitly bookable or not
- **Clearer UI:** No generated time-range confusion
- **Simpler pricing:** ₱X per hour × number of hours selected
- **Easier conflict detection:** Check each TimeSlot, not overlapping ranges
- **Better matches real-world:** Courts typically book by the hour

**Differences from Prior Approach:**

- **Before:** Arbitrary StartTime/EndTime, generated time-range combinations, potential UI confusion
- **After:** 24 fixed hourly TimeSlots, explicit per-TimeSlot availability, clearer continuous selection

## One Booking Per Reservation

**Why?**

- **Simpler database:** One record = one reservation
- **Easier admin:** One booking reference = one viewable transaction
- **Better reporting:** Duration calculated from one record
- **Clearer status:** One status per reservation, not per hour
- **Customer experience:** Booking a 4-hour session is simpler (not 4 separate interactions)

## Server-Side Validation & Price Calculation

**Why?**

- **Security:** Customer cannot manipulate price or status
- **Authoritative:** Server is always right, frontend is informational
- **Race protection:** Final check happens atomically in database
- **Compliance:** Price matches configured rules every time

---

# 24. TENANT-OWNED DATA

> **Planned — Phase 20 (Multi-Tenant Database Foundation).** This section
> documents the approved **future** multi-tenant data model. It does **not**
> change the current single-organization implementation, which continues to
> operate as described in Sections 1–23 until Phase 20 is implemented.

All tenant-owned data must be **isolated by `OrganizationId`**.

At minimum, the following data must be reviewed and made tenant-scoped:

- **Court**
- **Booking**
- **Pricing**
- **Court-specific availability / maintenance**
- **Organization settings**
- **Payment**
- **Payment settings**

The **global 24 TimeSlot definitions may remain shared/global** (they are the
atomic hourly unit of booking and are not necessarily tenant-owned).

Every tenant-owned query must be filtered by `OrganizationId` so that data from
one organization is never visible to another.

---

# 25. MANUAL GCASH PAYMENT

> **Planned — Phase 24 (Manual GCash Payment).** This section documents the
> approved **future** manual payment workflow. It is **NOT a GCash API
> integration** and does **not** change the Version 1 "no payment" rule
> (Section 3). It is not implemented yet.

## Scope

Manual GCash payment is a **manual, human-verified** workflow. There is **no
GCash API integration**, **no automatic payment verification**, and **no online
payment gateway**.

## Customer Flow

1. Select court / date / continuous TimeSlots.
2. Enter customer information.
3. Create booking.
4. Display **organization-specific** GCash instructions.
5. Display **organization-specific** GCash QR.
6. Customer pays manually.
7. Customer enters GCash reference number.
8. Customer may upload payment screenshot / proof.
9. Organization admin reviews.
10. Admin verifies or rejects payment.

## Suggested Payment Model

Fields:

- **Id**
- **OrganizationId**
- **BookingId**
- **Amount**
- **PaymentMethod**
- **PaymentStatus**
- **ReferenceNumber**
- **ProofImageUrl**
- **SubmittedAt**
- **VerifiedAt**
- **VerifiedBy**

### Payment Statuses

- **Pending**
- **Submitted**
- **Verified**
- **Rejected**
- **Cancelled**

**Security rule:** Customers must **never** be able to mark their own payment as
**Verified**. Only an authorized organization admin can verify or reject.

## Organization Payment Settings

Add `OrganizationPaymentSettings`:

- **Id**
- **OrganizationId**
- **PaymentMethod**
- **AccountName**
- **AccountNumber**
- **QRCodeUrl**
- **Instructions**
- **IsActive**

## Payment Proof Storage

Payment proof storage must be **tenant-aware** so one organization can never
read another organization's proof files.

Example logical path:

```
payment-proofs/{organization-slug}/{booking-reference}/...
```

---

# 26. PLATFORM ADMINISTRATION

> **Planned — Phase 27 (Platform Administration).** This section documents the
> approved **future** platform-level administration model.

## PlatformAdmin Capabilities

A **PlatformAdmin** may:

- Create organizations
- View organizations
- Activate / deactivate organizations
- Manage organization membership
- Assign subscription plans
- View subscription status
- View high-level platform information

## Organization Admin Scope

**Organization admins must only manage their own organization.**

They must never manage, view, or modify another organization's data, bookings,
payments, or settings.

---

# 27. ORGANIZATION

> **Planned — Phase 20 (Multi-Tenant Database Foundation).** The `Organization`
> model is the primary tenant abstraction. **Do NOT use `CourtOwner`** as the
> tenant abstraction.

## Organization Model

Fields:

- **Id**
- **Name**
- **Slug**
- **Description**
- **LogoUrl**
- **Phone**
- **Email**
- **Address**
- **TimeZone**
- **Currency**
- **Status**
- **CreatedAt**
- **UpdatedAt**

**`Slug` must be unique.**

The slug is used to resolve the organization from the request subdomain (see
Section 30).

---

# 28. ORGANIZATION MEMBERS

> **Planned — Phase 20 (Multi-Tenant Database Foundation).** Organization
> membership links a platform user to an organization with a role.

## OrganizationMember Model

Fields:

- **Id**
- **OrganizationId**
- **UserId**
- **Role**
- **CreatedAt**

## Roles

- **PlatformAdmin** — Manages the whole platform (all organizations).
- **OrganizationOwner** — Owns and manages a single organization.
- **OrganizationAdmin** — Administers a single organization.
- **OrganizationStaff** — Operates within a single organization with limited
  privileges.

Role and membership are always scoped to an organization (except
`PlatformAdmin`, which is platform-wide).

---

# 29. TENANT ISOLATION

> **Planned — Phase 21 (Tenant Context & Isolation) and audited in Phase 28
> (Security & Tenant Isolation Audit).** These are mandatory requirements for
> the multi-tenant platform.

## Isolation Requirements

- Tenant A must **never** see Tenant B data.
- Tenant A must **never** modify Tenant B data.
- Tenant A must **never** access Tenant B bookings.
- Tenant A must **never** access Tenant B payment information.
- Tenant A must **never** access Tenant B payment proof files.
- `OrganizationId` must **never** be trusted from customer/client input.
- Tenant must be resolved **server-side**.
- Tenant filtering must be enforced in **services / database queries**.
- **Cross-tenant automated tests are mandatory.**

## Enforcement Principles

- Never accept `OrganizationId` from request bodies, query strings, or hidden
  form fields.
- Resolve the tenant server-side (from the resolved organization context) and
  apply it to every tenant-owned query.
- Centralize tenant filtering in the service layer to avoid one-off, easily
  forgotten filters.

---

# 30. SUBDOMAIN TENANT RESOLUTION

> **Planned — Phase 22 (Subdomain Tenant Resolution).** The subdomain identifies
> the organization.

## Resolution Flow

```
Request hostname
  → subdomain / slug
  → Organization lookup
  → Organization context
  → tenant-aware services
  → tenant-specific data
```

Example:

```
pikolball.example.com
  → pikolball
  → Organization
  → OrganizationId
```

## Reserved Platform Subdomains

Reserved platform subdomains may include:

- **www**
- **app**
- **admin**
- **api**
- **mail**
- **support**

These are **not** organization subdomains and must not resolve to an
organization.

## Local Development

Local development must have a **safe tenant-resolution strategy** before
production DNS is configured (e.g., a resolvable default/test organization so
the application does not crash or leak data when no real subdomain exists).

---

# 31. ORGANIZATION BRANDING

> **Planned — Phase 23 (Organization Management & Branding).** Future
> organization customization includes:

- Organization name
- Logo
- Description
- Contact information
- Address
- Branding
- Booking settings

The **customer-facing booking page must use the current organization's
information** (name, logo, description, contact, address, branding, booking
settings).

---

# 32. EMAIL / GMAIL NOTIFICATIONS

> **Planned — Phase 25 (Email / Gmail Notifications).** Email is a **notification
> channel, NOT the database**. The database remains the source of truth.

## Implementation Approach

- Initial implementation should use a **server-side email service abstraction**.
- Do **NOT** require every organization owner to connect a personal Gmail
  initially.
- Optional **per-organization Gmail OAuth** may be considered later.

## Customer Notifications (may include)

- Booking received
- Payment instructions
- Payment submitted
- Payment verified
- Payment rejected
- Booking cancelled

## Organization Notifications (may include)

- New booking
- Payment submitted
- Booking cancellation

**Emails must use organization-specific information.**

---

# 33. SUBSCRIPTIONS

> **Planned — Phase 26 (Subscription Management).** Initially support **manual
> activation**. Do **NOT** implement online billing yet.

## Suggested SubscriptionPlan Model

- **Id**
- **Name**
- **Price**
- **BillingPeriod**
- **MaxCourts**
- **MaxStaff**
- **Features**
- **IsActive**

## Suggested Subscription Model

- **Id**
- **OrganizationId**
- **PlanId**
- **Status**
- **StartDate**
- **EndDate**
- **TrialEndDate**

### Subscription Statuses

- **Trial**
- **Active**
- **Expired**
- **Suspended**
- **Cancelled**

Online subscription billing is **out of scope at this stage**.

---

# 34. EXISTING PIKOLBALL DATA MIGRATION

> **Planned — Phase 20 (Multi-Tenant Database Foundation).** Existing Pikolball
> data becomes **Organization #1**.

Migration must:

- Create **Organization #1**.
- Assign existing **courts** to Organization #1.
- Assign existing **bookings** to Organization #1.
- Assign existing **pricing** to Organization #1.
- **Preserve booking history.**
- **Preserve existing functionality.**
- **Avoid deleting existing data.**

---

# 35. PRESERVED ARCHITECTURE & CONSTRAINTS

> These constraints are **preserved** and must **not** be changed by the
> multi-tenant evolution.

## Preserved (required to keep)

- C#
- ASP.NET Core (.NET 10)
- Razor Pages
- EF Core
- PostgreSQL / Supabase
- Bootstrap
- ASP.NET Core Identity
- Existing service-layer architecture
- Fixed 24 hourly TimeSlots
- Consecutive TimeSlot selection
- One Booking per reservation
- Server-side availability
- Server-side pricing
- Double-booking protection
- Anonymous customer booking
- Existing admin functionality

## Must NOT Introduce

- React
- Angular
- Vue
- Next.js
- Node.js backend
- TypeScript
- Microservices
- Redis
- Kubernetes
- Separate database per tenant
- Custom domains for organizations as a requirement
- Automatic GCash payment gateway
- Online subscription billing at this stage

---

# 36. IMPLEMENTATION STATUS

This document represents the **approved design**, and the application now
implements it.

- **Application code** — implemented for Phases 1–18 (booking model redesign,
  fixed hourly TimeSlots, availability, pricing, admin, schedule, UI/UX).
- **Database migrations** — applied to the PostgreSQL/Supabase database
  (booking range foundation, period exclusion constraint, TimeSlot seed data,
  legacy booking-time-slot column drop).
- **Existing functionality** — preserved; no existing workflows were broken.
- **Phase 19 (UI Polish)** — in progress; current work is presentation-only
  (toast notifications and confirmation dialogs).

## Current Implementation Status

- **Phases 1–18 — Complete.**
- **Phase 19 — UI Polish — In Progress.**
- **Phases 20–30 — Planned** (multi-tenant SaaS evolution).

The multi-tenant SaaS requirements in Sections 24–35 describe the **approved
future architecture** and are **not yet implemented**.

---
