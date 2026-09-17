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

> Phases 16 and 17 are complete. Phase 18 is in progress (see below).

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

### Phase 18 — UI/UX Refinement (In Progress)

**Objective:** Improve overall application UI/UX using Bootstrap 5.

**Status:** In progress. Homepage, navigation, Bootstrap Icons, and the brand design system
are implemented in the working tree (uncommitted). Remaining items are pending.

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

**Pending:**

- [ ] Refine Booking Experience (current UI carried over from Phase 17; further polish pending)
- [ ] Improve Schedule/Calendar View (still a plain Bootstrap table)
- [ ] Accessibility improvements (partial: skip link + aria labels; full audit pending)
- [ ] Mobile responsiveness review (partial; formal review pending)
- [ ] Error message refinement (partial)
- [ ] Loading state indicators (not started)

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

## Design Principles for Phases 16–18

### Fixed 1-Hour TimeSlots

- 24 standard hourly TimeSlots (12:00 AM through 11:00 PM)
- Each TimeSlot is explicitly Active or Inactive
- Availability is determined per TimeSlot
- Bookings span one or more consecutive TimeSlots
- One Booking record per reservation (not per hour)

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

## Phase 19 — UI Polish

* Loading states
* Empty states
* Error states
* Confirmation dialogs
* Toast notifications
* Better validation feedback
* Accessibility
* Mobile optimization
* Consistent typography
* Consistent spacing
* Consistent components

## Phase 20 — Regression and Final Testing

* Run all existing automated tests
* Run new booking tests
* Run availability tests
* Run pricing tests
* Test overlap scenarios
* Test concurrent booking scenarios
* Test booking lookup
* Test admin functionality
* Test desktop UI
* Test tablet UI
* Test mobile UI
* Fix regressions


## Phase 21
Automated Testing

## Phase 22
Production Preparation

## Phase 23
Deployment

