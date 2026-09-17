# TODO - Pickleball Booking System

## Development Environment & Setup

- [x] Verify development environment
- [x] Create ASP.NET Core project
- [ ] Configure GitHub (push to repository)
- [ ] Configure Copilot
- [ ] Configure Supabase

## Core Application (Phases 1–15)

- [x] Create database models
- [x] Create migrations
- [x] Build Court Management
- [x] Build Time Slot Management
- [x] Build Pricing
- [x] Build Customer Booking
- [x] Build Availability
- [x] Prevent Double Booking
- [x] Build Booking Lookup
- [x] Build Admin Authentication
- [x] Build Admin Dashboard
- [x] Build Booking Management
- [x] Build Schedule
- [x] Add Automated Tests
- [x] Security Review

## Documentation Update (Current)

- [x] Update PROJECT_REQUIREMENTS.md for fixed hourly TimeSlot design
- [x] Update DEVELOPMENT_PLAN.md with new phase descriptions
- [x] Update TODO.md
- [x] Review README.md
- [x] Document Phase 18 completed vs pending items

## Phase 16 — Booking Model Redesign (Complete)

- [x] Inspect existing Booking implementation
- [x] Inspect existing TimeSlot implementation  
- [x] Plan migration strategy
- [x] Redesign Booking model to use StartTime/EndTime
- [x] Support multi-TimeSlot continuous bookings
- [x] Update pricing for per-TimeSlot calculation
- [x] Update availability logic
- [x] Update overlap protection
- [x] Migrate existing data
- [x] Update tests
- [x] Build and test
- [x] Verify no regressions

> Migration: `20260918090000_DropLegacyBookingTimeSlotColumn`.

## Phase 17 — Fixed-TimeSlot Availability UI (Complete)

- [x] Display all 24 hourly TimeSlots
- [x] Show TimeSlot availability status
- [x] Implement continuous selection
- [x] Display duration and price
- [x] Responsive design
- [x] Mobile layout
- [x] Updated tests

## Phase 18 — UI/UX Refinement (In Progress)

- [x] Improve Homepage (hero, dynamic court cards, how-it-works, benefits, location, FAQ, CTA)
- [x] Enhance Navigation (branded navbar, footer, active-link highlighting, admin dropdown)
- [x] Bootstrap Icons integration (navbar, cards, buttons, footer)
- [x] Brand design system in site.css (CSS variables, buttons, cards, forms, badges)
- [ ] Refine Booking Experience (current UI carried over from Phase 17; further polish pending)
- [ ] Improve Schedule/Calendar View (still plain table; redesign pending)
- [ ] Error message refinement (partial; further review pending)
- [ ] Accessibility improvements (partial: skip link, aria labels; full audit pending)
- [ ] Mobile responsiveness review (partial; formal review pending)
- [ ] Loading state indicators (not started)

> Note: Phase 18 work is currently **uncommitted working-tree changes**. Commit once reviewed.

## Deployment

- [ ] Deploy to production
