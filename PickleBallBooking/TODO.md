# TODO - Pickleball Booking System

## Development Environment & Setup

- [x] Verify development environment
- [x] Create ASP.NET Core project
- [x] Configure GitHub (repository pushed, merged to `main`)
- [ ] Configure Copilot
- [x] Configure Supabase (connection string via User Secrets; migrations applied)

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

## Documentation Update

- [x] Update PROJECT_REQUIREMENTS.md for fixed hourly TimeSlot design
- [x] Update DEVELOPMENT_PLAN.md with new phase descriptions
- [x] Update TODO.md
- [x] Review README.md

- [x] Document Phase 18 completion
- [x] Remove redundant point-in-time documents (availability/fix summaries)

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

## Phase 18 — UI/UX Refinement (Complete)

- [x] Improve Homepage (hero, dynamic court cards, how-it-works, benefits, location, FAQ, CTA)
- [x] Enhance Navigation (branded navbar, footer, active-link highlighting, admin dropdown)
- [x] Bootstrap Icons integration (navbar, cards, buttons, footer)
- [x] Brand design system in site.css (CSS variables, buttons, cards, forms, badges)
- [x] Refine Booking Experience (step indicator, legend, past-slot disabling, sticky summary)
- [x] Improve Schedule/Calendar View (redesigned schedule grid + status legend)
- [x] Error message refinement (consistent alert styling)
- [x] Loading state indicators (page overlay + per-button spinners)

> Phase 18 is complete and merged to `main` (`c509b9a`).

## Phase 19 — UI Polish (In Progress)

- [x] Toast notifications (inline alerts enhanced into floating toasts; no logic changes)
- [x] Confirmation dialogs (reusable modal replaces native confirm for destructive actions)
- [ ] Empty states
- [ ] Better validation feedback
- [ ] Component consistency

> Presentation-only changes. No functionality, business logic, or POST handlers altered.

## Phase 20 — Multi-Tenant Database Foundation

- [ ] Analyze entire repository
- [ ] Add Organization
- [ ] Add OrganizationMember
- [ ] Define roles
- [ ] Identify tenant-owned entities
- [ ] Add OrganizationId
- [ ] Update EF relationships
- [ ] Update indexes/constraints
- [ ] Create Organization #1
- [ ] Migrate existing Pikolball data
- [ ] Create migration
- [ ] Apply migration
- [ ] Run existing tests
- [ ] Add tenant foundation tests

## Phase 21 — Tenant Context & Isolation

- [ ] IOrganizationContext
- [ ] Tenant-aware services
- [ ] Tenant-aware queries
- [ ] Authorization
- [ ] Cross-tenant read tests
- [ ] Cross-tenant write tests

## Phase 22 — Subdomain Tenant Resolution

- [ ] Define subdomain strategy
- [ ] Resolve tenant from hostname
- [ ] Unknown tenant handling
- [ ] Reserved hosts
- [ ] Local development strategy
- [ ] Multiple subdomain testing

## Phase 23 — Organization Management & Branding

- [ ] Organization profile
- [ ] Logo
- [ ] Contact details
- [ ] Branding
- [ ] Organization settings
- [ ] Organization member management

## Phase 24 — Manual GCash Payment

- [ ] Payment model
- [ ] Payment settings
- [ ] GCash QR
- [ ] GCash instructions
- [ ] Reference number
- [ ] Screenshot/proof upload
- [ ] Admin verification
- [ ] Admin rejection
- [ ] Tenant-safe storage
- [ ] Payment tests

## Phase 25 — Email / Gmail Notifications

- [ ] Email service abstraction
- [ ] Booking notification
- [ ] Payment notification
- [ ] Verification notification
- [ ] Rejection notification
- [ ] Cancellation notification
- [ ] Organization notification
- [ ] Organization-aware templates

## Phase 26 — Subscription Management

- [ ] SubscriptionPlan
- [ ] Subscription
- [ ] Trial
- [ ] Active
- [ ] Expired
- [ ] Suspended
- [ ] Manual activation
- [ ] Plan limits

## Phase 27 — Platform Administration

- [ ] PlatformAdmin
- [ ] Organization management
- [ ] Organization activation/deactivation
- [ ] Member management
- [ ] Subscription management
- [ ] Platform dashboard

## Phase 28 — Security & Tenant Isolation Audit

- [ ] Cross-tenant tests
- [ ] Authorization tests
- [ ] Crafted URL tests
- [ ] Crafted form tests
- [ ] Payment proof access tests
- [ ] Storage security review
- [ ] Logging review
- [ ] Secrets review

## Phase 29 — Final UI/UX & Production Readiness

- [ ] Empty states
- [ ] Validation
- [ ] Responsive review
- [ ] Accessibility review
- [ ] Tenant branding review
- [ ] Production configuration
- [ ] Backup/recovery
- [ ] Final regression

## Phase 30 — Deployment

- [ ] Production deployment
- [ ] Database migration
- [ ] Environment configuration
- [ ] Primary domain
- [ ] Wildcard subdomain
- [ ] DNS
- [ ] Storage
- [ ] Email
- [ ] GCash
- [ ] Multi-tenant smoke test
- [ ] Final security verification
