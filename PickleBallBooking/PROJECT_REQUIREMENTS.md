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

**Phase 18** — UI/UX Refinement
- Bootstrap 5 improvements
- Modern responsive layout
- Accessibility enhancements
- Error message refinement

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

# 24. DOCUMENTATION-ONLY STATUS

This document represents the **approved new design**.

**No application code has been modified.**

**No database migrations have been applied.**

**No existing functionality has been broken.**

Code and migrations will align to these requirements in future phases (subject to approval).

---
