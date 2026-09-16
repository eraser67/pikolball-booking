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

# 5. TIME SLOTS

Create a TimeSlots table.

Fields:

* Id
* StartTime
* EndTime
* Status

Administrators can:

* Add
* Edit
* Activate
* Deactivate

Time slots must be dynamic.

Do not hard-code time slots in customer booking pages.

---

# 6. BOOKINGS

Create a Bookings table.

Fields:

* Id
* BookingReference
* CustomerName
* CustomerPhone
* CustomerEmail
* CourtId
* BookingDate
* TimeSlotId
* Price
* BookingStatus
* CreatedAt
* UpdatedAt

BookingStatus:

* Pending
* Confirmed
* Cancelled
* Completed

Initial booking status:

Pending

---

# 7. PRICING

Create a Pricing table.

Fields:

* Id
* DayType
* StartTime
* EndTime
* Price
* Status
* CreatedAt
* UpdatedAt

DayType:

* Weekday
* Weekend

Prices must be configurable by administrators.

Do not hard-code final business prices.

Example development prices:

Weekday 08:00–17:00 = ₱300

Weekday 17:00–22:00 = ₱400

Weekend 08:00–22:00 = ₱400

These are sample values only.

---

# 8. BOOKING FLOW

Customer:

1. Open website.
2. Select date.
3. View available courts.
4. Select court.
5. Select available time.
6. View calculated price.
7. Enter name.
8. Enter mobile number.
9. Enter email.
10. Review booking.
11. Submit booking.
12. Receive booking reference.

Do not require customer registration in Version 1.

---

# 9. AVAILABILITY

Availability is based on:

Court
+
Booking Date
+
Time Slot

Example:

Court 1
September 20
10:00–11:00

If already booked:

NOT AVAILABLE

Otherwise:

AVAILABLE

The server must perform the final availability check.

Do not rely only on frontend checks.

---

# 10. DOUBLE BOOKING PROTECTION

This is a critical business rule.

The system must never allow two active bookings for:

Same Court
+
Same Date
+
Same Time Slot

Before creating a booking:

1. Check availability.
2. Validate again on the server.
3. Reject if unavailable.
4. Return a friendly message.
5. Use a database-level uniqueness constraint where appropriate.

Cancelled bookings should not block the slot.

The system must handle two customers attempting to book the same slot at approximately the same time.

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

Build the application to be testable.

Create automated tests using xUnit where appropriate.

Test:

1. Booking creation.
2. Past booking date rejection.
3. Inactive court rejection.
4. Inactive time slot rejection.
5. Double booking rejection.
6. Cancelled booking releases slot.
7. Correct price calculation.
8. Unique booking reference.
9. Booking lookup authorization.
10. Admin authorization.
11. Court management.
12. Time slot management.
13. Pricing management.

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

✓ View website
✓ Select date
✓ View courts
✓ View available slots
✓ Select court
✓ Select time
✓ See price
✓ Enter name
✓ Enter mobile
✓ Enter email
✓ Submit booking
✓ Receive booking reference
✓ Look up booking
✓ Cannot book past date
✓ Cannot double-book

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

Do not build the whole system.

First inspect the existing repository and determine whether the basic ASP.NET Core Razor Pages project is correctly configured.

Then report:

1. Current project structure.
2. .NET version.
3. Existing packages.
4. Existing configuration.
5. Any problems you find.

Do not make major changes until I approve the plan.

After my approval, begin Phase 1.
