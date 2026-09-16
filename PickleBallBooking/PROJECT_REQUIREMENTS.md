# PICKLEBALL BOOKING SYSTEM

## MASTER PROJECT REQUIREMENTS

You are a senior C#/.NET software architect and full-stack developer.

Help me build a simple, maintainable, responsive web-based Pickleball Court Booking System.

The system must initially cost ₱0 to develop and should use free/open-source technologies and free tiers wherever practical.

I am a Test Automation Engineer with C# experience, but I am not an expert ASP.NET Core developer.

Therefore:

* Explain important decisions.
* Give clear implementation steps.
* Tell me exactly which files need to be created or changed.
* Provide complete code when creating files.
* Do not assume I know where code belongs.
* Keep the implementation simple.
* Do not over-engineer.

---

# 1. TECHNOLOGY

Use:

* C#
* ASP.NET Core
* Razor Pages
* Entity Framework Core
* PostgreSQL
* Supabase PostgreSQL free tier
* Bootstrap
* ASP.NET Core Identity
* Git
* GitHub
* Visual Studio Community

Do NOT use:

* React
* Angular
* Vue
* Next.js
* Node.js backend
* TypeScript
* Microservices
* Redis
* Kubernetes
* Docker unless specifically required
* Paid APIs
* Paid SaaS services

Use a simple monolithic architecture.

Architecture:

Customer/Admin
↓
ASP.NET Core Razor Pages
↓
Services
↓
Entity Framework Core
↓
PostgreSQL / Supabase

---

# 2. VERSION 1 SCOPE

Version 1 must provide:

CUSTOMER:

* Home page
* Court availability
* Date selection
* Court selection
* Time slot selection
* Price display
* Customer name
* Mobile number
* Email
* Booking submission
* Booking reference
* Booking status
* Booking lookup

ADMIN:

* Secure login
* Dashboard
* Court management
* Time slot management
* Pricing management
* Booking management
* Booking confirmation
* Booking cancellation
* Booking completion
* Daily schedule
* Basic filtering/search

---

# 3. PAYMENT

There is NO PAYMENT FUNCTIONALITY in Version 1.

Do NOT implement:

* GCash API
* PayMongo
* Xendit
* Stripe
* PayPal
* Credit cards
* Payment gateway
* Automatic payment verification
* SMS payment verification

The system must work completely without online payment.

Payment may be added in a future version.

---

# 4. COURTS

Create a dynamic Courts table.

Fields:

* Id
* Name
* Description
* Status
* CreatedAt
* UpdatedAt

Status:

* Active
* Inactive

Courts must NOT be hard-coded.

The administrator must be able to:

* Add court
* Edit court
* Activate court
* Deactivate court

If the admin adds Court 4, it must automatically appear on the customer booking page.

Do not require code changes.

Do not permanently delete courts that have booking history.

Prefer deactivation.

---

# 5. TIME CONFIGURATION

The system must support configurable booking time boundaries.

The existing TimeSlots concept must NOT automatically be deleted.

Before changing the existing TimeSlots implementation, inspect how it is currently used.

Time configuration may be used to define:

Allowed booking start times
Allowed booking end times
Booking intervals
Operating hours
Other configurable time boundaries required by the booking system

The administrator must be able to:

Add time configuration
Edit time configuration
Activate time configuration
Deactivate time configuration

Time configuration must be dynamic.

Do not hard-code booking times into customer booking pages.

The final design must allow the customer to create a continuous booking using a StartTime and EndTime.

Example:

Start Time:
18:00

End Time:
22:00

Result:

4-hour continuous booking.

The exact implementation must be determined after inspecting the existing TimeSlot model, services, pages, and database structure.

Do not remove the existing TimeSlot table or functionality until the impact has been assessed.

---

# 6. BOOKINGS

Create and maintain a Bookings table.

The booking represents a continuous reservation for a court during a specific date and time range.

The target booking fields should include:

Id
BookingReference
CustomerName
CustomerPhone
CustomerEmail
CourtId
BookingDate
StartTime
EndTime
DurationHours
Price
BookingStatus
CreatedAt
UpdatedAt

The final database design must be determined after inspecting the existing implementation.

The existing TimeSlotId must not be removed blindly.

If existing booking records use TimeSlotId, determine how existing data can be migrated safely.

Booking Time

A booking consists of:

BookingDate
+
CourtId
+
StartTime
+
EndTime

Example:

Date:
September 20, 2026

Court:
Court 2

Start:
18:00

End:
22:00

Duration:
4 hours

The customer should create this as ONE booking.

The customer should not be required to create four separate one-hour bookings.

Booking Status

BookingStatus:

Pending
Confirmed
Cancelled
Completed

Initial booking status:

Pending

Cancelled bookings must not block future availability.

Time Validation

The system must reject:

EndTime earlier than StartTime
EndTime equal to StartTime
Booking outside configured operating hours
Invalid booking intervals
Invalid time ranges

The system must calculate DurationHours from StartTime and EndTime.

Duration must not be trusted from the client.

The server must calculate the duration.

---

# 7. PRICING

MULTI-HOUR BOOKING PRICING

Pricing must support bookings that span multiple hours.

The system must calculate the price from:

Booking date
StartTime
EndTime
DayType
Applicable pricing configuration

Example:

Booking:

18:00–22:00

Duration:

4 hours

If the applicable rate is:

₱400/hour

Then:

4 × ₱400 = ₱1,600

The price must be calculated server-side.

The customer must not be able to submit an arbitrary price.

If a booking crosses multiple pricing periods, the system must calculate the applicable rate for each portion of the booking.

Example:

16:00–18:00

If:

16:00–17:00 = ₱300/hour

17:00–22:00 = ₱400/hour

Then the system must calculate:

1 × ₱300
+
1 × ₱400

Total:

₱700

The final pricing algorithm must be implemented using the existing Pricing configuration and must be covered by automated tests.

---

# 8. BOOKING FLOW

The customer booking experience should use a modern calendar-style interface.

Customer:

Open website.
Open the booking/calendar page.
Select a booking date.
View available courts.
Select a court.
Select a Start Time.
Select an End Time.
System validates the selected time range.
System calculates the booking duration.
System calculates the applicable price.
Customer reviews the booking.
Customer enters name.
Customer enters mobile number.
Customer enters email.
Customer submits the booking.
System performs a final server-side availability check.
System creates the booking.
System displays the booking reference.

Example:

Date:
September 20, 2026

Court:
Court 2

Start:
6:00 PM

End:
10:00 PM

Duration:
4 hours

Price:
Calculated from the configured pricing rules.

The customer should create one booking for the complete continuous period.

Do not require customer registration in Version 1.

---

# 9. AVAILABILITY

Availability is based on:

Court
+
Booking Date
+
Requested StartTime
+
Requested EndTime

The system must determine whether the requested continuous time range overlaps an existing active booking.

Example

Existing booking:

Court 2
September 20
18:00–20:00

Requested booking:

Court 2
September 20
19:00–22:00

Result:

NOT AVAILABLE

Reason:

The requested period overlaps the existing booking.

Back-to-Back Booking

Existing:

18:00–20:00

Requested:

20:00–22:00

Result:

AVAILABLE

The end time of one booking may equal the start time of another booking.

Cancelled Booking

Cancelled bookings must not block availability.

Availability Status

The customer-facing interface should clearly distinguish:

Available
Booked
Unavailable
Selected
Server Validation

The server must perform the final availability check immediately before creating the booking.

The frontend availability display must never be treated as authoritative.

The system must protect against two customers attempting to book overlapping periods at approximately the same time.

---

# 10. DOUBLE BOOKING PROTECTION

This is a critical business rule.

The system must never allow two active bookings for the same:

Court
Booking Date
Overlapping time period

The system must detect overlapping time ranges.

Conceptually, a requested booking overlaps an existing active booking when:

ExistingStartTime < RequestedEndTime

AND

ExistingEndTime > RequestedStartTime

Example 1 — Overlap

Existing:

18:00–20:00

Requested:

19:00–22:00

Result:

REJECTED

Example 2 — Overlap

Existing:

18:00–22:00

Requested:

19:00–20:00

Result:

REJECTED

Example 3 — Back-to-Back

Existing:

18:00–20:00

Requested:

20:00–22:00

Result:

ALLOWED

Example 4 — Before Existing Booking

Existing:

18:00–20:00

Requested:

16:00–18:00

Result:

ALLOWED

Protection Requirements

Before creating a booking:

Check availability.
Validate the requested time range.
Validate the court.
Validate the booking date.
Validate operating hours.
Perform the final server-side overlap check.
Use appropriate database constraints, transactions, or concurrency protection.
Reject the booking if an overlapping active booking exists.
Return a friendly message to the customer.

The implementation must handle two customers attempting to reserve overlapping periods at approximately the same time.

Cancelled bookings must not block the requested period.

---

# 11. BOOKING REFERENCE

Generate a unique booking reference.

Example:

PB-20260920-0001

The reference must be unique.

Display it after successful booking.

---

# 12. BOOKING LOOKUP

Customers do not need an account.

Allow booking lookup using:

Booking Reference
+
Customer Mobile Number or Email

Only return the matching booking.

Do not expose another customer's booking.

---

# 13. ADMIN

Use ASP.NET Core Identity.

Protect all Admin pages.

Only authorized administrators can access:

/Admin

/Admin/Bookings

/Admin/Courts

/Admin/TimeSlots

/Admin/Pricing

Do not store passwords manually.

Do not place credentials in source code.

Use secure configuration for secrets.

---

# 14. ADMIN DASHBOARD

Show:

Today's Bookings

Pending Bookings

Confirmed Bookings

Completed Bookings

Active Courts

Provide links to:

* Bookings
* Courts
* Time Slots
* Pricing
* Schedule

Keep dashboard simple.

---

# 15. ADMIN BOOKING MANAGEMENT

Display:

* Booking Reference
* Customer
* Court
* Date
* Time
* Price
* Status
* Created Date

Allow filtering by:

* Date
* Court
* Status
* Customer

Actions:

* View
* Confirm
* Cancel
* Complete

Validate status transitions.

---

# 16. RESPONSIVE UI

Use Bootstrap.

The application must work on:

* Desktop
* Laptop
* Tablet
* Mobile

Customer booking must be easy to use on a mobile phone.

Use simple:

* Cards
* Forms
* Buttons
* Tables
* Status indicators

Avoid unnecessary animations.

---

# 17. VALIDATION

Server-side validation is mandatory.

Validate:

* Customer name required
* Mobile required
* Valid email
* Booking date required
* Booking date cannot be in the past
* Court must be active
* Time slot must be active
* Slot must be available
* Price must exist
* Booking reference must be unique

Never trust client-provided:

* Price
* Status
* Court availability

---

# 18. SECURITY

Implement:

* Authentication
* Authorization
* Anti-forgery protection
* Server-side validation
* Secure configuration
* Proper database access
* Protection against unauthorized booking lookup

Avoid:

* Hard-coded secrets
* Password storage
* Sensitive logging
* Exposing stack traces to customers

---

# 19. ERROR HANDLING

Customers should receive friendly errors.

Example:

"Sorry, this time slot is no longer available. Please select another time."

Do not expose:

* Stack traces
* Database errors
* Connection strings
* Internal exceptions

Use ASP.NET Core logging for technical errors.

---

# 20. TESTING

# ADDITIONAL BOOKING AND CALENDAR TESTS

Automated tests must include:

1. Valid multi-hour booking creation.
2. StartTime is stored correctly.
3. EndTime is stored correctly.
4. Duration is calculated correctly.
5. EndTime before StartTime is rejected.
6. EndTime equal to StartTime is rejected.
7. Booking outside operating hours is rejected.
8. Overlapping booking is rejected.
9. Back-to-back booking is allowed.
10. Existing booking completely contains requested booking.
11. Requested booking completely contains existing booking.
12. Partial overlap at the beginning.
13. Partial overlap at the end.
14. Cancelled booking does not block availability.
15. Correct price for a multi-hour booking.
16. Correct price when crossing pricing periods.
17. Client-provided price cannot override server-calculated price.
18. Inactive court is rejected.
19. Past booking date is rejected.
20. Two concurrent booking attempts cannot both create overlapping bookings.
21. Booking reference remains unique.
22. Booking lookup continues to work.
23. Existing admin booking functionality continues to work.
24. Existing booking status transitions continue to work.


---

# 21. SAMPLE DATA

Development seed data:

Courts:

* Court 1
* Court 2
* Court 3

Time slots:

08:00–09:00
09:00–10:00
10:00–11:00
11:00–12:00
12:00–13:00
13:00–14:00
14:00–15:00
15:00–16:00
16:00–17:00
17:00–18:00
18:00–19:00
19:00–20:00
20:00–21:00
21:00–22:00

Sample prices:

Weekday 08:00–17:00 = ₱300

Weekday 17:00–22:00 = ₱400

Weekend 08:00–22:00 = ₱400

These are development values and must be editable through Admin.

---

# 22. PROJECT STRUCTURE

Prefer:

PickleballBooking/

Data/

Models/

Services/

Pages/

wwwroot/

Tests/

Do not create unnecessary projects or layers.

---

# 23. DEVELOPMENT PROCESS

IMPORTANT:

Do NOT build the entire application in one step.

Build incrementally.

Phase 1:
Environment/project setup

Phase 2:
Database configuration

Phase 3:
Database models

Phase 4:
Court management

Phase 5:
Time slot management

Phase 6:
Pricing

Phase 7:
Customer booking

Phase 8:
Availability

Phase 9:
Double booking protection

Phase 10:
Booking lookup

Phase 11:
Admin authentication

Phase 12:
Admin dashboard

Phase 13:
Booking management

Phase 14:
Schedule/calendar

Phase 15:
Validation/error handling/security

Phase 16:
Automated testing

Phase 17:
Production preparation

Phase 18:
Deployment

After completing each major phase:

1. Build the application.
2. Run tests.
3. Check for compilation errors.
4. Explain what was implemented.
5. Provide a manual testing checklist.
6. Wait for my confirmation before moving to the next major phase.

Do not skip phases.

---

# 24. AI DEVELOPMENT RULES

Before making changes:

1. Read this PROJECT_REQUIREMENTS.md.
2. Inspect the existing project.
3. Understand existing code.
4. Do not overwrite working functionality unnecessarily.
5. Follow existing conventions.

When adding a feature:

1. Explain the feature.
2. Identify files to create/change.
3. Implement it.
4. Build the application.
5. Run relevant tests.
6. Fix errors.
7. Summarize changes.
8. Give me manual test steps.

Do not make unrelated changes.

Do not add technologies that are not required.

---

# 25. CODE DELIVERY RULE

When giving instructions for manual changes, always provide:

FILE:
path/to/file.cs

Then provide the complete code or clearly identify the exact section to change.

Never give unexplained code fragments.

---

# 26. COST

The project should initially target ₱0 cost.

Prefer:

* Free Visual Studio Community
* Free GitHub
* Free/open-source .NET
* Free Bootstrap
* Supabase free tier
* Free-tier hosting

Identify anything that may eventually require payment.

Do not add paid services without asking me first.

---

# 27. VERSION 1 ACCEPTANCE CRITERIA

CUSTOMER:

CUSTOMER:

✓ View website
✓ Open modern booking/calendar interface
✓ Navigate/select booking date
✓ View active courts
✓ View court availability
✓ Select court
✓ Select Start Time
✓ Select End Time
✓ Book multiple consecutive hours as one booking
✓ See calculated duration
✓ See calculated price
✓ Cannot select an invalid time range
✓ Cannot select an unavailable period
✓ Cannot overlap another active booking
✓ Can book back-to-back with another booking
✓ Enter name
✓ Enter mobile
✓ Enter email
✓ Submit booking
✓ Receive booking reference
✓ Look up booking
✓ Cannot book past date
✓ Cannot manipulate the server-calculated price
✓ Booking works on mobile

ADMIN:

✓ Login
✓ Dashboard
✓ Manage courts
✓ Manage time slots
✓ Manage pricing
✓ View bookings
✓ Search/filter bookings
✓ Confirm booking
✓ Cancel booking
✓ Complete booking
✓ View schedule

SYSTEM:

✓ C#
✓ ASP.NET Core
✓ Razor Pages
✓ Entity Framework Core
✓ PostgreSQL
✓ Supabase
✓ Bootstrap
✓ Responsive
✓ Secure
✓ Dynamic courts
✓ Dynamic time slots
✓ Configurable pricing
✓ Double-booking protection
✓ Automated tests
✓ No payment system
✓ No paid service dependency

---

# CURRENT TASK

# CURRENT TASK

The Pickleball Booking System has already completed the original Phases 1–15.

The current task is to redesign the booking and availability experience while preserving the existing working application.

## IMPORTANT

Do NOT immediately modify the application.

First inspect the existing repository and implementation.

Read:

* PROJECT_REQUIREMENTS.md
* DEVELOPMENT_PLAN.md
* TODO.md
* README.md

Then inspect:

* Booking model
* Court model
* TimeSlot model
* Pricing model
* ApplicationDbContext
* EF Core configurations
* Existing migrations
* Booking services
* Availability services
* Pricing services
* Customer booking pages
* Admin booking pages
* Existing validation
* Existing database constraints
* Existing automated tests

## New Booking Requirement

The customer must be able to create a continuous booking by selecting:

1. Date
2. Court
3. Start Time
4. End Time

Example:

Court 2
September 20, 2026
6:00 PM–10:00 PM

This is ONE booking with:

Duration = 4 hours

## New Availability Requirement

Availability must be calculated using:

Court
+
Booking Date
+
StartTime
+
EndTime

The system must prevent overlapping active bookings.

Back-to-back bookings are allowed.

Cancelled bookings do not block availability.

## New UI Requirement

The customer booking experience should eventually use a modern calendar/schedule-style interface.

Use Bootstrap 5 and Bootstrap Icons.

Keep the existing ASP.NET Core Razor Pages architecture.

Do not introduce React, Angular, Vue, Next.js, Node.js, or a separate frontend application.

## Copilot Working Rule

Do not make code changes yet.

Do not create migrations yet.

Do not delete TimeSlot functionality yet.

First provide:

1. Current Booking architecture
2. Current TimeSlot architecture
3. Current Pricing architecture
4. Current Availability architecture
5. Current database schema
6. Existing database constraints
7. Existing booking tests
8. Proposed Booking model changes
9. Proposed TimeSlot changes
10. Proposed pricing changes
11. Proposed availability algorithm
12. Proposed overlap protection
13. Proposed database migration
14. Existing files that need modification
15. New files that may be required
16. Tests that need modification
17. New tests required
18. Data migration riskss
19. Recommended implementation order

Wait for approval before making any implementation changes.

Do not make unrelated changes.

# MODERN CALENDAR BOOKING UI

The customer booking interface should use a modern calendar/schedule-style experience.

The design should be inspired by modern reservation and sports facility booking interfaces.

## Customer Calendar

The customer should be able to:

* Navigate between dates
* Select a date
* View available courts
* View booked periods
* Select a court
* Select a Start Time
* Select an End Time
* Visually see the selected continuous period
* See the calculated duration
* See the calculated price
* Continue to booking

## Desktop

Prefer a schedule/calendar layout where practical.

Example:

| Time  | Court 1   | Court 2   | Court 3   |
| ----- | --------- | --------- | --------- |
| 18:00 | Available | Booked    | Available |
| 19:00 | Available | Booked    | Available |
| 20:00 | Available | Available | Available |
| 21:00 | Available | Available | Available |

A customer selecting:

18:00–22:00

should see the entire selected period visually represented as one continuous selection.

## Mobile

Do not force a wide desktop calendar onto a mobile screen.

Use a mobile-friendly layout that may present:

* Date selector
* Court selector
* Start time
* End time
* Selected booking summary

The booking process must remain easy to use on a phone.

## UI Technology

Use:

* Bootstrap 5
* Bootstrap Icons
* HTML
* CSS
* Minimal JavaScript

Do not introduce React, Angular, Vue, Next.js, Node.js, or a separate frontend application.

A lightweight JavaScript calendar/scheduling component may be considered only if it is compatible with the existing ASP.NET Core Razor Pages architecture and does not introduce unnecessary complexity or paid dependencies.


