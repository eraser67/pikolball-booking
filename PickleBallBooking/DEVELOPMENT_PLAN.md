# Pickleball Booking System Development Plan

## Phase 1
Environment and project verification

## Phase 2
PostgreSQL / Supabase configuration

## Phase 3
Entity Framework Core models and migrations

## Phase 4
Court Management

## Phase 5
Time Slot Management

## Phase 6
Pricing Management

## Phase 7
Customer Booking

## Phase 8
Availability

## Phase 9
Double Booking Protection

## Phase 10
Booking Lookup

## Phase 11
Admin Authentication

## Phase 12
Admin Dashboard

## Phase 13
Booking Management

## Phase 14
Schedule

## Phase 15
Security and Error Handling

# Phase 16 — Court Availability

## Goal

Allow customers to check court availability before starting the booking process.

## Requirements

* Customer availability page
* Date selection
* Display active courts dynamically
* Display active time slots dynamically
* Court/time-slot availability matrix
* Available status
* Booked status
* Unavailable status
* Real-time server-side availability check
* Only active courts can be displayed as bookable
* Only active time slots can be displayed as bookable
* Selecting an available court and time slot
* Display selected court, date, and time
* Continue to booking
* Pass selected booking details to the booking page
* Revalidate availability when the customer proceeds with booking
* Revalidate availability again when the booking is submitted
* Mobile responsive layout
* Automated tests

## Important Rules

* Do not duplicate or replace existing booking business logic unnecessarily.
* Reuse the existing availability and booking services where possible.
* Preserve the existing double-booking protection.
* Pending and Confirmed bookings must follow the existing business rules when determining availability.
* Cancelled bookings must not block a time slot.
* Do not rely only on client-side availability checks.
* The server must remain the source of truth.

---

# Phase 17 — UI/UX Redesign

## Goal

Modernize the customer and administrator user interface without unnecessarily changing working business logic or database functionality.

## Technology

Use:

* Existing ASP.NET Core Razor Pages architecture
* Bootstrap 5
* Bootstrap Icons
* Existing CSS structure
* Minimal JavaScript only when necessary

Do not introduce:

* React
* Angular
* Vue
* Next.js
* Node.js backend
* TypeScript
* Another frontend framework

## Requirements

### Shared Layout

* Modern responsive navigation
* Consistent header
* Consistent footer
* Mobile navigation
* Consistent spacing
* Consistent buttons
* Consistent cards
* Consistent form controls
* Consistent status indicators

### Customer Pages

Redesign:

* Homepage
* Court availability page
* Booking pages
* Booking review page
* Booking confirmation page
* Booking lookup page

The customer experience should be:

Simple
Modern
Professional
Sports-oriented
Easy to use on mobile

### Admin Pages

Redesign:

* Admin navigation
* Admin dashboard
* Court management
* Time slot management
* Pricing management
* Booking management
* Schedule pages

Use:

* Dashboard cards
* Responsive tables
* Clear action buttons
* Status badges
* Empty states
* Better spacing and layout

## Design Rules

* Preserve existing functionality.
* Do not rewrite working services unless necessary.
* Do not change the database schema unless required for a specific approved feature.
* Do not remove existing features.
* Keep the UI consistent across all pages.
* Use Bootstrap components before creating unnecessary custom components.
* Ensure all pages are responsive.

---

# Phase 18 — UI Polish and User Experience

## Goal

Improve the user experience and make the application feel more complete and professional.

## Requirements

### Loading States

Add appropriate loading states for operations such as:

* Checking availability
* Loading booking information
* Submitting booking
* Updating admin data

### Empty States

Provide clear empty states.

Examples:

* No bookings found
* No courts available
* No available time slots
* No search results

### Error Messages

Use friendly and consistent error messages.

Do not expose technical exceptions to users.

### Confirmation Dialogs

Use confirmation dialogs for important actions such as:

* Cancel booking
* Deactivate court
* Deactivate time slot
* Delete functionality if introduced later

### Toast Notifications

Add consistent success and error notifications for actions such as:

* Booking created
* Booking updated
* Court added
* Court updated
* Status changed
* Error occurred

### Forms

Improve:

* Labels
* Validation messages
* Required field indicators
* Input spacing
* Button placement
* Mobile usability

### Tables

Improve:

* Responsive behavior
* Empty states
* Status badges
* Search/filter usability
* Action button layout

### Accessibility

Improve:

* Proper form labels
* Keyboard navigation
* Visible focus states
* Sufficient contrast
* Accessible buttons
* Clear error messages

### Mobile Optimization

Test and optimize:

* Navigation
* Availability matrix
* Booking forms
* Admin tables
* Dashboard cards
* Buttons
* Date selection
* Court/time slot selection

## Important Rules

* Preserve existing functionality.
* Do not introduce unnecessary JavaScript dependencies.
* Do not introduce paid UI libraries.
* Prefer Bootstrap 5 and Bootstrap Icons.
* Keep performance and simplicity in mind.


## Phase 19
Automated Testing

## Phase 20
Production Preparation

## Phase 21
Deployment

