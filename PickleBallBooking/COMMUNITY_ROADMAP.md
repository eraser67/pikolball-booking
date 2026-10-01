# Punit Bola -- Community & Competitive Feature Roadmap

**Status:** Phases 31–36 COMPLETE (Release 1: Community Foundation in progress)

**Roadmap Continues From:** Phase 36 (Notifications -- Complete)

**Next Available Phase:** Phase 37 (Calendar Integration)

> **Note on Phase Numbering:** Phases 1-30 are documented in `DEVELOPMENT_PLAN.md` and are complete.
> Phases 26-30 in the existing plan cover Subscription Management, Platform Administration, Security Audit,
> Final UI/UX, and Production Deployment respectively. The community and competitive feature roadmap
> begins at Phase 31 to avoid any numbering conflict.

---

## Architectural Principles for All Future Phases

These principles govern every phase in this roadmap. Implementors must read and respect them before beginning any phase.

### 1. Booking Remains Independent

The existing court booking engine (`BookingService`, `CourtService`, `PricingService`) must not be redesigned or modified to accommodate community features. Court bookings and event participation (Open Play, Activities, Tournaments) are separate business concepts that share underlying resources (courts, time slots) but must remain independently operable.

### 2. Multi-Tenant Isolation is Mandatory

Every entity introduced in future phases must carry `OrganizationId` unless it is explicitly designed as a platform-level shared resource.

`
No tenant may access another tenant's:
  - Players / Profiles
  - Activities
  - Bookings
  - Matches
  - Tournaments
  - Rankings
  - Payments
  - Analytics
`

Cross-tenant discovery (e.g., a platform-wide "Find a Game" page) must be an explicitly designed opt-in mechanism controlled by the tenant, not a leakage in query filters.

### 3. Customer Accounts Are Platform-Level

Customer/Player accounts belong to the Punit Bola platform, not to any individual tenant. A player registers once and can participate in multiple tenant communities. Their relationship to each tenant (activity history, match records, standings) is tenant-scoped. Their core identity (name, email, skill level) is platform-owned.

### 4. Fixed Hourly TimeSlot Architecture is Preserved

The current 24 fixed hourly TimeSlot model (00:00-01:00 through 23:00-00:00) is the foundation for court reservations. Future phases that require court time (Activities, Tournaments, Leagues) must integrate with this model conceptually. Do not replace it with a free-form StartTime/EndTime continuous booking model.

### 5. Payment Architecture Reuse

The existing manual GCash payment workflow (`Payment`, `OrganizationPaymentSettings`, Supabase private bucket proof storage, admin verification portal) should be treated as an integration point for all future paid features. Do not build a parallel payment infrastructure. Extend the existing flow where possible.

### 6. This Is a Roadmap -- Not a Technical Specification

Phase descriptions capture **requirements, goals, dependencies, constraints, and expected behavior**. Detailed schema design, API endpoint definition, UI layout, and package selection happen when each phase is actually started.

---

## Priority Summary Table

| Release               | Phases | Purpose                                    | Status  |
|-----------------------|--------|--------------------------------------------|---------|
| Community Foundation  | 31-37  | Customer accounts, players, activities     | PLANNED |
| Venue Operations      | 38-39  | Attendance and court management            | PLANNED |
| Competitive           | 40-43  | Matches and rankings                       | PLANNED |
| Tournaments & Leagues | 44-48  | Competitions and leagues                   | PLANNED |
| Community             | 49-53  | Social engagement                          | PLANNED |
| Integrations          | 54-55  | DUPR and advanced notifications            | PLANNED |
| Discovery & SaaS      | 56-60  | Platform-wide expansion                    | PLANNED |
| Mobile                | 61     | PWA / mobile experience                    | PLANNED |

---

# RELEASE 1 -- COMMUNITY FOUNDATION

---

## Phase 31 -- Customer Account Registration & Authentication

**Status:** COMPLETE (Implemented)

### Goal

Enable customers and players to create their own platform-level accounts so they can manage
their own bookings, player profile, and community participation without relying on anonymous
booking flows alone.

This phase establishes the **customer identity layer** -- the foundation that all subsequent
community, competitive, and discovery features depend on.

### Rationale

Currently, customers book courts anonymously (Name, Phone, Email only, no persistent account).
This works for simple one-off reservations but cannot support player profiles, RSVP history,
match history, tournament registration, or cross-tenant community features.

Customer accounts must be distinct from admin accounts. A registered customer is a
**Player/Customer** role, not an OrganizationAdmin or OrganizationStaff. Tenant admins manage
their own venue and community; the customer remains a participant.

### Registration Flow

`
Customer visits punitbola.tech (or any tenant subdomain)
          |
Clicks "Sign Up" or "Create Account"
          |
Enters:
  - Full Name
  - Email address
  - Password
  - Mobile number
          |
Account created with role: Customer / Player
          |
Email verification sent (optional -- configurable)
          |
Customer redirected to their Player Dashboard
`

### Customer Capabilities After Registration

| Capability                          | Phase Available |
|-------------------------------------|-----------------|
| Manage Player Profile               | Phase 32        |
| Book courts (authenticated)         | Existing + Phase 31 |
| Join Open Play / Activities         | Phase 33        |
| RSVP to events                      | Phase 34        |
| Join waitlists                      | Phase 34        |
| Upload GCash payment proof          | Existing (enhanced) |
| View bookings and activities        | Phase 32 dashboard |
| Check match history / statistics    | Phase 43        |
| Register for tournaments / leagues  | Phase 45, 48    |

### Account Model

**Platform-Level Identity:**
The customer account belongs to the Punit Bola platform, not to a specific tenant. A customer
registers once and can then participate in any tenant community on the platform.

**Tenant-Specific Relationship:**
When a customer participates at a specific venue (books a court, joins an activity, competes
in a tournament), a tenant-scoped membership/participation record links them to that
organization. This record carries `OrganizationId`.

**Why This Matters for Later Phases:**
- Phase 57 (Find a Game): A player browses activities from multiple tenants using a single account.
- Phase 58 (Multi-Tenant Marketplace): A player can RSVP to events at different venues without
  re-registering at each one. Their participation data at each venue remains isolated.

### Authentication Design Considerations

> **Do not implement -- document only.**

Possible entities:
- `CustomerAccount` (or extend ASP.NET Core Identity `ApplicationUser` with a `Player` role)
- `PlayerTenantMembership` -- records the player's association with a specific organization

Key design questions for implementation time:
- Does the customer sign up at a tenant subdomain or the platform root? Recommendation: support both.
- Does email verification block login or simply prompt for verification?
- Can a customer link their existing anonymous bookings (matched by email) to their new account?
- How does the existing anonymous booking flow coexist with authenticated booking?
  Recommendation: preserve anonymous booking for customers who do not want to register.

### Role Separation

| Role                | Scope           | Created By          | Purpose                        |
|---------------------|-----------------|---------------------|--------------------------------|
| PlatformAdmin       | Platform-wide   | Platform owner      | Manage all tenants             |
| OrganizationOwner   | Tenant-specific | Platform Admin      | Own and manage their venue     |
| OrganizationAdmin   | Tenant-specific | OrganizationOwner   | Manage venue settings and staff|
| OrganizationStaff   | Tenant-specific | OrganizationAdmin   | Venue operational staff        |
| **Customer/Player** | **Platform-wide** | **Self-registration** | **Book courts, join events** |

Customer/Player accounts must not have access to any admin pages or management functions.

### Dependencies

- Existing: ASP.NET Core Identity (authentication infrastructure)
- Existing: Anonymous booking flow (must be preserved alongside authenticated flow)
- Existing: `OrganizationId` tenant isolation
- Required by: Phase 32 (Player Profile), and all subsequent phases

---

## Phase 32 -- Player Account & Profile Foundation

**Status:** COMPLETE (Implemented)

### Goal

Transform the registered customer account (Phase 31) into a rich pickleball player profile.
Registration creates the identity; this phase adds the player-specific information and dashboard.

### Planned Features

**Profile Information:**
- Profile photo / avatar upload
- Display name (publicly visible)
- First name and last name
- Mobile number (private by default)
- Preferred playing hand (Right / Left / Ambidextrous)
- Skill level (see below)
- Short bio / about text
- Location (city / region -- not precise address)
- Date joined the platform
- Privacy settings per field

**Player Dashboard:**
- Upcoming court bookings (linked to existing booking system via email/account match)
- Past court bookings
- Upcoming activities (Phase 33)
- Past activities (Phase 33)
- Match history (Phase 40)
- Tournament history (Phase 44)
- Achievements and badges (Phase 53)

**Skill Level (Initial):**

| Level        | Description                                      |
|--------------|--------------------------------------------------|
| Beginner     | New to pickleball, learning fundamentals         |
| Intermediate | Comfortable with play, developing consistency    |
| Advanced     | Competitive player, strong technique             |

Future extensions (not in Phase 32):
- DUPR rating (Phase 54)
- Admin-assigned rating
- Self-assessed granular rating (e.g., 2.5, 3.0, 3.5, 4.0, 4.5, 5.0)

### Potential Domain Entities

> **Do not implement these. Document only.**

- `PlayerProfile` -- Core profile record linked to the CustomerAccount (Phase 31).
- `PlayerPreference` -- Privacy settings, notification preferences, hand preference.
- `PlayerSkill` -- Skill level record, source (self-assessed / admin-assigned / DUPR), and history.

### Multi-Tenant Requirement -- Recommended Approach

**Option C -- Hybrid Model (Recommended):**
One global authentication identity and core profile (display name, photo, skill level) owned
by the player. Tenant-specific participation records (activity history, match history within
that club) are scoped by `OrganizationId`.

The player's identity and basic profile belong to the platform. Participation history, match
records, and standings belong to the tenant where they occurred. Tenant admins see only the
participation data within their own organization. Players choose whether their global profile
is discoverable across tenants.

### Dependencies

- Requires: Phase 31 (Customer Account Registration)
- Existing: `OrganizationId` tenant isolation
- Required by: Phases 33-61 (all)

---

## Phase 33 -- Open Play / Activities

**Status:** COMPLETE (Implemented)

### Goal

Allow tenant admins to create organized pickleball activities that players can browse and join.
Activities are separate from court bookings. The venue operator manages court allocation for the
activity; participating players do not create individual court bookings.

### Activity Types (Examples)

Open Play, Social Play, Training Session, Private Event, Drill Clinic.

### Activity Information

| Field               | Description                                                     |
|---------------------|-----------------------------------------------------------------|
| Activity Name       | Short descriptive title                                         |
| Description         | Rich text description of the activity                           |
| Date                | Calendar date                                                   |
| Start Time          | Activity start time                                             |
| End Time            | Activity end time                                               |
| Courts              | One or more courts assigned to the activity                     |
| Capacity            | Maximum number of players                                       |
| Skill Level         | Target skill level (Beginner / Intermediate / Advanced / Open)  |
| Format              | Activity format (Open Play, Round Robin, etc.)                  |
| Price (per player)  | Philippine Peso amount; 0 for free activities                   |
| Registration Opens  | Datetime when player registration becomes available             |
| Registration Closes | Datetime when registration closes                               |
| Status              | See Activity States below                                       |

### Activity States

| State               | Description                                                  |
|---------------------|--------------------------------------------------------------|
| Draft               | Created by admin, not yet visible to players                |
| Published           | Visible to players, registration not yet open               |
| Registration Open   | Players can join                                             |
| Full                | Capacity reached; new joiners go to waitlist (Phase 34)     |
| Registration Closed | Registration period has ended                                |
| In Progress         | Activity is currently occurring                              |
| Completed           | Activity has finished                                        |
| Cancelled           | Activity was cancelled by admin                              |

### Customer (Player) Capabilities

- Browse published activities for their current tenant
- View activity details (date, time, courts, skill level, capacity, price)
- Join an activity (RSVP -- Phase 34)
- Cancel participation (within allowed cancellation window)
- View current participant count and available spots

### Relationship to Existing Booking System

Activities use courts but do not create individual player bookings. The venue operator may
optionally block the relevant court time slots (via the existing Maintenance flag or a new
"Activity Block" mechanism) to prevent double-allocation of courts. Do not redesign the
booking engine.

### Dependencies

- Requires: Phase 32 (Player Profile)
- Requires: Existing Court model (court assignment)
- Requires: Existing `OrganizationId` isolation
- Required by: Phase 34, 35, 36, 37, 38, 39

---

## Phase 34 -- RSVP & Waitlist

**Status:** COMPLETE (Implemented)

### Goal

Allow players to register for activities with explicit RSVP tracking and automatic waitlist
management when an activity reaches capacity.

### RSVP States

| State      | Description                                                         |
|------------|---------------------------------------------------------------------|
| Requested  | Player has submitted a join request (pending admin or auto-confirm) |
| Confirmed  | Player's spot is confirmed                                          |
| Waitlisted | Activity is full; player is on the waitlist                         |
| Cancelled  | Player cancelled or admin removed them                              |
| Checked-In | Player was checked in on the day (Phase 38)                         |
| No-Show    | Player did not appear (Phase 38)                                    |

### Waitlist Behavior

- Each activity has a `MaxCapacity` set by the admin.
- When capacity is reached, new registrations automatically enter the waitlist.
- Waitlist position determined by registration timestamp (first registered = first position).
- Admin may manually reorder the waitlist if needed.
- When a confirmed player cancels, the next waitlisted player is automatically promoted.
- Automatic promotion fires a notification (Phase 36).
- Admins may manually promote a waitlisted player regardless of order.

### Payment Integration (Future -- Do Not Implement Now)

Paid activities will integrate with the existing manual GCash payment workflow:

`
Player requests to join activity
    > Activity has a price (> 0)
    > Player directed to GCash payment page
    > Player submits reference number + optional proof screenshot
    > Admin reviews and verifies payment
    > RSVP status: Requested to Confirmed
`

For free activities, RSVP can be auto-confirmed without payment review. Extend the existing
`Payment` and `OrganizationPaymentSettings` models when implemented.

### Dependencies

- Requires: Phase 33 (Activities)
- Requires: Phase 32 (Player Profile)
- Required by: Phase 36 (Notifications), Phase 38 (Check-In)

---

## Phase 35 -- Recurring Activities

**Status:** COMPLETE ✅

**Completed:** 2026-10-01

### Goal

Allow tenant admins to define a recurring activity template that automatically generates
individual activity instances on a schedule.

### What Was Implemented

#### New Models
- **`RecurrenceType` enum** — `Weekly`, `Monthly`, `SpecificDays`
- **`ActivitySeriesStatus` enum** — `Active`, `Paused`, `Cancelled`, `Completed`
- **`ActivitySeries` model** — recurring template with full recurrence config, scheduling window,
  and occurrence defaults (name, format, skill level, time, capacity, price)
- **`Activity.SeriesId`** — nullable FK back-reference linking occurrences to their parent series

#### Recurrence Options

| Option        | Description                                          |
|---------------|------------------------------------------------------|
| Weekly        | Repeats on selected days of the week                 |
| Monthly       | Repeats on a specific day of the month               |
| Specific Days | Custom set of dates                                  |
| Start Date    | First occurrence date                                |
| End Date      | Last occurrence date (or no end date / open-ended)   |

#### New Service: `ActivitySeriesService`
- Create series + generate up to 200 occurrences on creation
- Pause / Resume series (resume regenerates missing future occurrences)
- Cancel entire series (cancels all future non-completed occurrences)
- Cancel series from a given date onward
- Cancel a single occurrence without affecting siblings
- Bulk-update future occurrences when series template is edited
- `GetCurrentCourtIdsAsync` for pre-filling edit forms

#### Admin Pages (Razor Pages)
- **`/Admin/Activities/Series`** — series index with recurrence type, date range, occurrence count
- **`/Admin/Activities/Series/Create`** — form with dynamic JS panels per recurrence type, day-of-week checkboxes
- **`/Admin/Activities/Series/{id}`** — series detail with full occurrence table + lifecycle action buttons
- **`/Admin/Activities/Series/{id}/Edit`** — edit form with apply-to-future-occurrences option

#### Navigation Integration
- "Recurring Series" button added to Activities index header
- "Series" back-link badge on each occurrence in the Activities index
- "Recurring" breadcrumb + badge on Activity detail pages for series occurrences

#### Database (Migration `Phase35_RecurringActivitySeries`)
- New `ActivitySeries` table with all recurrence fields
- Nullable `SeriesId` FK column on `Activities` table
- Indexes: `IX_ActivitySeries_OrganizationId_Status`, `IX_Activities_SeriesId`
- FK constraints with `RESTRICT` delete behavior (series deletion does not cascade-delete occurrences)

### Isolation from Existing Booking System

Recurring activities generate `Activity` records (Phase 33), not `Booking` records.
The existing fixed hourly time-slot booking system is completely unmodified.

### Tenant Isolation

`ActivitySeries` participates in the same EF global query filter pattern as all other
tenant-owned types. Added to `TenantOwnedTypes` hash set in `ApplicationDbContext`.

### Dependencies

- Requires: Phase 33 (Activities)
- Requires: Phase 34 (RSVP)
- Existing: Court model, Fixed TimeSlot model (conceptual alignment only — not modified)

---

## Phase 36 -- Notifications

**Status:** COMPLETE (Implemented)

### Goal

Extend the existing email notification system to cover community activity events and introduce
in-app notification infrastructure.

### Customer / Player Notifications

| Event              | Description                                                 |
|--------------------|-------------------------------------------------------------|
| Activity Published | Notify subscribed players when a new activity goes live     |
| RSVP Confirmed     | Confirmation that player's spot is secured                  |
| RSVP Cancelled     | Confirmation that player's cancellation was processed       |
| Waitlisted         | Notify player they are on the waitlist                      |
| Waitlist Promotion | Notify player their waitlist spot became confirmed          |
| Payment Received   | Acknowledge activity payment submission                     |
| Payment Approved   | Activity payment verified; spot confirmed                   |
| Payment Rejected   | Activity payment rejected with reason                       |
| Activity Cancelled | Notify all registered players of cancellation               |
| Activity Reminder  | Reminder sent N hours before activity start                 |

### Admin / Staff Notifications

| Event            | Description                                                 |
|------------------|-------------------------------------------------------------|
| New Registration | Player joined an activity                                   |
| New Payment      | Payment submitted for an activity                           |
| Cancellation     | Player cancelled their RSVP                                 |
| Waitlist Update  | Waitlist changed (player promoted or cancelled)             |
| No-Show          | Player marked as no-show after activity                     |

### Notification Channels

| Channel    | Phase    | Notes                                                   |
|------------|----------|---------------------------------------------------------|
| Email      | Phase 36 | Extend existing BookingEmailService / IEmailService    |
| In-App     | Phase 36 | Simple notification feed visible on player dashboard   |
| Push (PWA) | Phase 61 | Browser push notifications via service worker          |
| Telegram        |    | Group Notification   |
| SMS        | Future   | Optional -- out of scope for initial implementation    |

### Dependencies

- Requires: Phase 33 (Activities), Phase 34 (RSVP)
- Existing: `BookingEmailService`, `IEmailService`
- Required by: Phase 34 (Waitlist promotion), Phase 38 (Check-In reminders)

---

## Phase 37 -- Calendar Integration

**Status:** PLANNED / FUTURE

### Goal

Allow players and admins to export and subscribe to activity, booking, and match schedules
via standard calendar formats.

### Supported Calendar Targets

| Calendar        | Format / Protocol            |
|-----------------|------------------------------|
| ICS File        | RFC 5545 .ics download        |
| Google Calendar | CalDAV / ICS import link      |
| Apple Calendar  | ICS / CalDAV subscription URL |
| Outlook         | ICS import / Exchange feed    |

### Exportable Items

| Item               | Phases Required     |
|--------------------|---------------------|
| Court Bookings     | Existing + Phase 31 |
| Activities         | Phase 33            |
| Tournament Matches | Phase 45            |
| League Matches     | Phase 48            |

### Dependencies

- Requires: Phase 33 (Activities), Phase 32 (Player Profile)
- Existing: Court booking model
- Required by: Phase 61 (PWA -- calendar integration)

---

# RELEASE 2 -- VENUE OPERATIONS

---

## Phase 38 -- Player Check-In

**Status:** COMPLETE

### Goal

Allow venue staff to record player attendance for activities and court bookings, enabling
no-show tracking and attendance history for analytics.

### Check-In Methods

| Method          | Description                                                        |
|-----------------|--------------------------------------------------------------------|
| QR Check-In     | Player presents a QR code on their phone; staff scans to check in |
| Admin Check-In  | Staff manually marks a player as checked in from admin dashboard   |
| Manual Check-In | Walk-in or verbal confirmation recorded by staff                   |

### Anticipated QR Check-In Flow

`
Player opens activity confirmation on mobile
    > Displays QR code (encoded: RSVP ID or Booking ID)
    > Venue staff scans QR code
    > System identifies the booking or RSVP
    > Check-in recorded; RSVP status changed to Checked-In
`

No-show history is visible on the player's profile (tenant-scoped). Attendance data feeds into analytics (Phase 59).

### Dependencies

- Requires: Phase 33 (Activities), Phase 34 (RSVP), Phase 32 (Player Profile)
- Required by: Phase 59 (Community Analytics)

---

## Phase 39 -- Court Assignment

**Status:** COMPLETE

### Goal

Allow venue staff to assign players to specific courts within an activity, supporting both
manual and skill-based grouping strategies.

### Assignment Modes

| Mode                 | Description                                                         |
|----------------------|---------------------------------------------------------------------|
| Manual Assignment    | Admin manually assigns each player to a court                      |
| Automatic Assignment | System distributes players evenly across available courts          |
| Skill-Based Grouping | Group players by skill level to courts                             |

### Management Capabilities

Assign Player, Move Player, Rebalance Players, Lock Assignment.

Court assignment operates on the existing `Court` entity. It does not modify `CourtTimeSlot`
records or create `Booking` records.

### Dependencies

- Requires: Phase 33 (Activities), Phase 34 (RSVP), Phase 32 (Player Profile)
- Existing: `Court` model
- Required by: Phase 40 (Round Robin)

---

# RELEASE 3 -- COMPETITIVE PICKLEBALL

---

## Phase 40 -- Round Robin Engine

**Status:** COMPLETE

### Goal

Support the generation and management of round robin match schedules within an activity.

### Supported Formats

| Format            | Description                                                        |
|-------------------|--------------------------------------------------------------------|
| Rotating Partners | Each round, players are rearranged into new doubles pairs         |
| Fixed Partners    | Doubles pairs remain constant; face different opponents each round |
| Singles           | Individual players rotate opponents                               |

### Configuration Inputs

Players, Courts, Number of Rounds, Scoring Rules (e.g., rally score to 11), Match Duration.

### Generated Output

- Match assignments per round: Player A + B vs C + D on Court X
- Schedule with estimated start times per round
- Match records ready for score entry (Phase 41)

Generated match assignments reference existing `Court` records. Do not create `Booking`
records for round robin matches.

### Dependencies

- Requires: Phase 33 (Activities), Phase 39 (Court Assignment), Phase 32 (Player Profile)
- Required by: Phase 41 (Match Scoring)

---

## Phase 41 -- Match Scoring

**Status:** PLANNED / FUTURE

### Goal

Allow players and admins to enter, track, and finalize match scores for round robin and
tournament matches.

### Match Lifecycle

| State       | Description                                   |
|-------------|-----------------------------------------------|
| Scheduled   | Match has been generated; no play yet         |
| In Progress | Play has started; scores being entered        |
| Completed   | Final scores entered and submitted            |
| Cancelled   | Match was cancelled                           |

### Scoring Capabilities

| Capability         | Description                                                  |
|--------------------|--------------------------------------------------------------|
| Match Creation     | Generated from Round Robin engine or Tournament engine       |
| Match Assignment   | Players / teams assigned to the match                        |
| Score Entry        | Enter game-by-game scores (e.g., 11-7, 11-9)                |
| Live Score         | Optional real-time score viewing during match                |
| Match Completion   | Player or admin submits final scores                         |
| Score Validation   | System checks scores are within expected game rules          |
| Admin Finalization | Admin reviews and finalizes the official result              |
| Score Correction   | Admin can correct a finalized score (with audit log)         |

Finalized scores must be protected from accidental modification. Any correction must go
through an admin override with an explicit reason logged.

### Dependencies

- Requires: Phase 40 (Round Robin Engine -- or Phase 44 Tournament Foundation)
- Requires: Phase 32 (Player Profile)
- Required by: Phase 42, 43, 44, 51, 54

---

## Phase 42 -- Standings & Leaderboards

**Status:** PLANNED / FUTURE

### Goal

Calculate and display standings and leaderboards based on finalized match results, scoped per tenant.

### Calculation Metrics

Wins, Losses, Games Played, Points Scored, Points Conceded, Point Differential, Win Percentage.

All calculations derive from finalized match records only. Leaderboards are always
tenant-specific (`OrganizationId` scoped). Competitive standings must remain separate from
community engagement leaderboards (Phase 52).

### Dependencies

- Requires: Phase 41 (Match Scoring -- finalized results)
- Required by: Phase 43 (Player Statistics), Phase 44 (Tournament Foundation)

---

## Phase 43 -- Player Statistics & Match History

**Status:** PLANNED / FUTURE

### Goal

Display accumulated player statistics and full match history on a player's profile, derived
entirely from finalized match results.

### Player Statistics

Games Played, Wins, Losses, Win Percentage, Points Scored, Points Conceded, Point Differential,
Partner History, Opponent History, Match History.

Statistics must be generated exclusively from finalized match records. Incomplete or cancelled
matches must not be included. Players may control visibility of their match history
(public / followers / private).

### Dependencies

- Requires: Phase 41 (Match Scoring), Phase 42 (Standings), Phase 32 (Player Profile)

---

# RELEASE 4 -- TOURNAMENTS & LEAGUES

---

## Phase 44 -- Tournament Foundation

**Status:** PLANNED / FUTURE

### Goal

Allow tenant admins to create and manage pickleball tournaments with multiple formats,
division management, and player or team registration.

### Tournament Types

| Type               | Description                                                    |
|--------------------|----------------------------------------------------------------|
| Single Elimination | Lose once and you're out; bracket-style                       |
| Round Robin        | All teams/players face each other; standings determine winner |
| Pool Play          | Group stage (round robin) followed by elimination bracket     |

### Tournament Information

Tournament Name, Date(s), Division(s) (e.g., Men's Open, Women's Open, Mixed Doubles, Senior 50+),
Skill Level, Format, Registration Opens/Closes, Entry Fee (Philippine Peso), Max Capacity,
Status (Draft > Published > Registration Open > In Progress > Completed/Cancelled).

### Dependencies

- Requires: Phase 32 (Player Profile), Phase 41 (Match Scoring), Phase 42 (Standings)
- Required by: Phase 45, 46, 47

---

## Phase 45 -- Tournament Registration

**Status:** PLANNED / FUTURE

### Goal

Allow players and teams to register for tournaments, with capacity management, deadline
enforcement, waitlist support, and payment integration.

Registration options: Individual (singles) or Team/Partner (doubles).
Features: Capacity Enforcement, Deadline Enforcement, Payment (existing GCash flow),
Approval, Cancellation, Waitlist.

Tournament entry fees use the same manual GCash proof-of-payment flow as court bookings.
Do not build a separate payment system.

### Dependencies

- Requires: Phase 44 (Tournament Foundation), Phase 34 (RSVP/Waitlist model)
- Existing: Manual GCash payment workflow

---

## Phase 46 -- Tournament Brackets

**Status:** PLANNED / FUTURE

### Goal

Generate, display, and manage tournament brackets based on confirmed registrations and seedings.

Capabilities: Automatic Seeding, Manual Seeding, Bracket Generation, Quarterfinals/Semifinals/Finals,
Match Advancement, Result Recording (Phase 41), Result Correction with audit log, Finalization.

Formats: Single Elimination, Round Robin Pool, Pool Play + Elimination.

### Dependencies

- Requires: Phase 44 (Tournament Foundation), Phase 45 (Tournament Registration), Phase 41 (Match Scoring)

---

## Phase 47 -- Tournament Scheduling

**Status:** PLANNED / FUTURE

### Goal

Generate and manage a match schedule for tournament rounds, including court assignment, start
times, match durations, rest periods, and conflict detection.

### Scheduling Inputs

Matches, Courts, Start Time, Match Duration, Rest Periods, Court Availability (must not
conflict with existing Booking records).

### Conflict Detection

- Player/team assigned to two courts at the same time
- Courts used for matches that overlap with existing Booking records
- Insufficient rest periods between consecutive matches for the same player/team

The tournament scheduler reads court availability but does not create Booking records.

### Dependencies

- Requires: Phase 46, Phase 44
- Existing: `Court` model, `CourtTimeSlot` model (read-only)

---

## Phase 48 -- League Management

**Status:** PLANNED / FUTURE

### Goal

Allow tenant admins to create and run ongoing competitive leagues with seasons, divisions,
team management, scheduling, standings, and playoff support.

### League Structure

`
League > Season(s) > Division(s) > Teams > Match Schedule > Matches > Scores > Standings > Playoffs > Championship
`

League Features: League Creation, Seasons (multi-season support), Divisions, Teams (register teams;
manage rosters), Schedule, Matches, Standings, Playoffs, Championship.

### Dependencies

- Requires: Phase 44 (Tournament Foundation), Phase 41 (Match Scoring), Phase 42 (Standings), Phase 32

---

# RELEASE 5 -- COMMUNITY

---

## Phase 49 -- Community Feed

**Status:** PLANNED / FUTURE

### Goal

Provide a tenant-scoped community feed where admins can post announcements, results, and updates
visible to all players registered with that organization.

### Feed Post Types

Admin Announcements, Event Announcements, Tournament Announcements, Venue Announcements,
Maintenance Notices, Results, Community Updates.

Only tenant admins and staff can create posts. No cross-tenant post visibility by default.

### Dependencies

- Requires: Phase 32 (Player Profile)
- Existing: `OrganizationId` isolation

---

## Phase 50 -- Player Connections

**Status:** PLANNED / FUTURE

### Goal

Allow players to connect with other players they have played with, view public profiles,
and invite players to activities.

Features: Follow Players (opt-in, not mutual), View Player Profile, Common Activities,
Invite Players, Privacy Controls.

Private contact information (phone, email) must never be exposed through player connections.
Players can block another player from following them.

### Dependencies

- Requires: Phase 32 (Player Profile), Phase 33 (Activities)

---

## Phase 51 -- Kudos / Recognition

**Status:** PLANNED / FUTURE

### Goal

Allow players to send optional positive recognition to other players they have played with.

Kudos Categories: Great Partner, Great Player, Good Sportsmanship, Fun to Play With.

Anti-Abuse: Players can only send kudos to players they have actually played with (verified via
match records). One kudos per category per other player per activity/event. Players can opt out.
Kudos counts are displayed on profiles but do not affect competitive standings.

### Dependencies

- Requires: Phase 50 (Player Connections), Phase 41 (Match Scoring -- verify "played with")

---

## Phase 52 -- Community Leaderboards

**Status:** PLANNED / FUTURE

### Goal

Recognize highly engaged community members through participation-based leaderboards that
are completely separate from competitive skill rankings.

Categories: Most Active (most check-ins), Most Games (total played), Most Events Joined,
Community Participation (combined engagement score).

Community leaderboards must never be presented as a measure of competitive skill.
Always `OrganizationId` scoped.

### Dependencies

- Requires: Phase 32 (Player Profile), Phase 38 (Check-In), Phase 34 (RSVP)

---

## Phase 53 -- Achievements / Badges

**Status:** PLANNED / FUTURE

### Goal

Reward player milestones with unlockable badges visible on their player profile.

### Example Badges

| Badge               | Earn Condition                                    |
|---------------------|---------------------------------------------------|
| First Game          | Complete first match                              |
| 10 Games Played     | Complete 10 total matches                         |
| 10 Wins             | Record 10 match wins                              |
| Tournament Champion | Win a tournament                                  |
| 10 Events Joined    | Attend 10 activities (checked-in)                 |
| Community Regular   | Attend at least 1 activity per month for 3 months |

Badge conditions are evaluated against finalized, verified data only.

### Dependencies

- Requires: Phase 32 (Player Profile), Phase 41 (Match Scoring), Phase 34 (RSVP), Phase 38 (Check-In)

---

# RELEASE 6 -- INTEGRATIONS

---

## Phase 54 -- DUPR Integration

**Status:** PLANNED / FUTURE -- External API Dependency

### Goal

Submit finalized match results to the DUPR (Dynamic Universal Pickleball Rating) platform.

> **Important:** This phase depends on DUPR providing a usable API or partner integration.
> Do not build until API requirements are confirmed with DUPR.

### Anticipated Flow

`
Match finalized in Punit Bola (Phase 41)
    > Both players review and approve match result for DUPR submission
    > Punit Bola submits match to DUPR API
    > DUPR processes result and updates player ratings externally
    > Punit Bola displays new DUPR rating on player profile (pulled from DUPR API)
`

### Design Considerations

| Concern              | Consideration                                                     |
|----------------------|-------------------------------------------------------------------|
| API Requirements     | DUPR partner API access -- must be confirmed before implementation |
| Authentication       | OAuth or API key -- TBD per DUPR documentation                    |
| Player Linking       | Players must link Punit Bola account to their DUPR profile        |
| Match Validation     | Only finalized, non-disputed matches eligible for submission      |
| Duplicate Prevention | System must not submit the same match twice                       |
| Error Handling       | Failed submissions must be retried safely with logging           |
| Privacy              | Player must explicitly opt in to DUPR submission                  |
| Tenant Isolation     | DUPR submissions must be scoped to the organization where the match occurred |

### Dependencies

- Requires: Phase 41 (Match Scoring), Phase 32 (Player Profile)
- External: DUPR API (partner access required)

---

## Phase 55 -- Advanced Notifications

**Status:** PLANNED / FUTURE

### Goal

Extend the notification system beyond email to support in-app notification feeds, browser push
notifications (via PWA service worker), and a configurable notification preference center.

### Notification Channels

| Channel       | Phase    | Implementation Notes                                    |
|---------------|----------|---------------------------------------------------------|
| Email         | Phase 36 | Already planned; extend existing IEmailService         |
| In-App Feed   | Phase 36 | Simple notification list on player dashboard            |
| Push (Browser)| Phase 55 | Service worker push via PWA (Phase 61 prerequisite)    |
| SMS           | Future   | Optional; out of scope for initial build                |

Notification Categories: Booking, Activity, Waitlist, Tournament, Match, Payment, Community.

Players should configure per-category preferences (Email On/Off, In-App On/Off, Push On/Off).

### Dependencies

- Requires: Phase 36 (Basic Notifications), Phase 32 (Player Profile)
- Optional: Phase 61 (PWA -- for push notifications)

---

# RELEASE 7 -- DISCOVERY & MULTI-TENANT COMMUNITY

---

## Phase 56 -- Find a Game (Game Discovery)

**Status:** PLANNED / FUTURE

### Goal

Introduce a discovery feature allowing registered players (Phase 31 accounts) to search for
activities, open play sessions, and tournaments using filters.

A player with one platform account can discover and join activities at any participating
tenant without re-registering.

### Discovery Filters

Location, Date, Time (Morning/Afternoon/Evening), Skill Level, Format
(Open Play/Round Robin/Tournament/League), Venue, Activity Type.

### Cross-Tenant Discovery Rules

- Tenants must explicitly opt in to make their activities publicly discoverable.
- Player data is never exposed in discovery results -- only activity metadata is shown.
- Tenant isolation for RSVP, payments, and participation data is fully maintained.

### Dependencies

- Requires: Phase 31 (Customer Account -- single platform identity)
- Requires: Phase 33 (Activities), Phase 44 (Tournament Foundation)
- Requires: Phase 57 (Multi-Tenant Marketplace -- for cross-tenant scope)

---

## Phase 57 -- Multi-Tenant Community Marketplace

**Status:** PLANNED / FUTURE

### Goal

Build a platform-level discovery experience where players can find courts, activities,
tournaments, and leagues from multiple participating tenant organizations through a single
Punit Bola platform entry point. This is made possible by the platform-level customer account
introduced in Phase 31.

### Platform Discovery Flow (Concept)

`
Player visits punitbola.tech (logged in with their platform account)
          |
Searches for: Open Play near Manila, Saturday 6PM, Intermediate
          |
Results show matching activities from opted-in tenant clubs
          |
Player selects activity > directed to that tenant's RSVP page
          |
RSVP and payment handled within the tenant's isolated environment
`

Default: activities are not platform-discoverable (private to tenant members). Tenants opt in
per-activity or globally. All player participation data remains 100% within the tenant's data
boundary.

### Dependencies

- Requires: Phase 31 (Customer Account -- platform identity enables cross-tenant participation)
- Requires: Phase 56 (Game Discovery)
- Requires: Phase 58 (Advanced Tenant Controls -- for discovery opt-in flags)
- Existing: `OrganizationId` isolation, `TenantResolutionMiddleware`

---

## Phase 58 -- Advanced Tenant Community Controls

**Status:** PLANNED / FUTURE

### Goal

Provide tenant admins with granular feature flags to enable or disable community and competitive
features for their specific organization.

### Feature Flags (Examples)

| Flag                        | Controls                                               |
|-----------------------------|--------------------------------------------------------|
| Enable Player Profiles      | Whether players can create profiles for this org       |
| Enable Open Play/Activities | Whether activities are available                       |
| Enable Waitlist             | Whether waitlists are active for activities            |
| Enable Tournaments          | Whether the tournament engine is available             |
| Enable Rankings/Standings   | Whether competitive standings are displayed            |
| Enable Community Feed       | Whether the community feed is available                |
| Enable Achievements         | Whether badges and achievements are active             |
| Enable DUPR Integration     | Whether DUPR match submission is enabled               |
| Public Discovery            | Whether activities appear in the platform marketplace  |

Feature flags could be stored as a JSON column on `Organization` or as a dedicated
`OrganizationFeatureFlags` entity. Do not implement now -- design at implementation time.

### Dependencies

- Requires: Phases 32-57 (various features that the flags control)
- Existing: `Organization` model

---

## Phase 59 -- Community Analytics

**Status:** PLANNED / FUTURE

### Goal

Provide tenant admins with a data-rich analytics dashboard covering booking patterns,
community engagement, and competitive activity metrics.

### Booking Analytics

Total Bookings, Revenue (Philippine Peso), Court Utilization, Popular Courts, Popular Times.

### Community Analytics

Active Players, New Players (new Phase 31 accounts joining the tenant), Events Created, RSVP Rate,
Cancellation Rate, Waitlist Conversion, Attendance Rate.

### Competitive Analytics

Matches Played, Active Players, Tournaments Held, Match Completion Rate.

### Dependencies

- Requires: Phase 38 (Check-In), Phase 41 (Match Scoring), Phase 44 (Tournament Foundation)
- Existing: Booking and payment data

---

## Phase 60 -- Advanced Match Scheduling

**Status:** PLANNED / FUTURE

### Goal

Design and document a future intelligent scheduling engine that can automatically generate
optimal match schedules given a set of constraints.

### Scheduling Engine Inputs

Players (with skill levels), Courts (with operating windows), Time Available, Match Duration,
Rest Period, Previous Partners (to avoid repeating), Previous Opponents (to avoid repeating).

### Scheduling Engine Outputs

Match Assignments (Player A + B vs C + D), Court Assignments, Round Schedule, Rest Periods.

This is an advanced feature best approached as an optimization algorithm. The first
implementation may be a simplified version that prioritizes correctness over optimal rotation.

### Dependencies

- Requires: Phase 40 (Round Robin Engine), Phase 47 (Tournament Scheduling), Phase 48 (League Management)
- Existing: `Court` model, `CourtTimeSlot` model

---

# RELEASE 8 -- MOBILE EXPERIENCE

---

## Phase 61 -- PWA / Mobile Experience

**Status:** PLANNED / FUTURE

### Goal

Transform the Punit Bola web application into a Progressive Web App (PWA) that can be installed
on a player's phone and provide a near-native mobile experience without requiring a separate
iOS or Android codebase.

### PWA Capabilities to Document

| Feature                 | Description                                                  |
|-------------------------|--------------------------------------------------------------|
| Installable Application | "Add to Home Screen" on iOS and Android                      |
| Service Worker          | Offline fallback and background sync capability              |
| Mobile Navigation       | Bottom navigation bar optimized for thumb reach              |
| Push Notifications      | Browser push for activity reminders, RSVP updates (Phase 55) |
| QR Scanning             | Camera access for player check-in scanning (Phase 38)        |
| Camera Upload           | Direct camera capture for payment proof uploads              |
| Calendar Integration    | One-tap add to device calendar (Phase 37)                    |
| Mobile Event Dashboard  | Player's upcoming activities, matches, and bookings          |
| Mobile Scoring          | Score entry optimized for phone use                          |

### Native App Strategy

The first implementation should remain a web PWA rather than creating separate native iOS or
Android applications. A PWA provides a single codebase, no app store submission required,
installable on both iOS and Android, push notification support, and device camera access.
Native apps should only be considered if PWA capabilities prove insufficient.

### Dependencies

- Requires: Phase 36 (Notifications), Phase 38 (Check-In -- QR scanning), Phase 37 (Calendar)
- Existing: Responsive Bootstrap 5 layout (Phase 18)

---

## Phase Dependency Map

`
Existing Foundation (Phases 1-30)
  [Courts] [Bookings] [Payments] [Organizations] [Subscriptions]
                    |
Phase 31 -- Customer Account Registration & Authentication
                    |
Phase 32 -- Player Profile
                    |
Phase 33 -- Open Play / Activities
           /              \
Phase 34 -- RSVP       Phase 35 -- Recurring Activities
     |                         |
Phase 36 -- Notifications <----+
     |
Phase 37 -- Calendar Integration
                    |
Phase 38 -- Player Check-In
                    |
Phase 39 -- Court Assignment
                    |
Phase 40 -- Round Robin Engine
                    |
Phase 41 -- Match Scoring
           /              \
Phase 42 -- Standings    Phase 43 -- Player Statistics
                    |
Phase 44 -- Tournament Foundation
           /    |    \
Phase 45  Phase 46  Phase 47 -- Registration / Brackets / Scheduling
                    |
Phase 48 -- League Management
                    |
[Phase 49-53: Community Feed, Connections, Kudos, Leaderboards, Achievements]
                    |
[Phase 54-55: DUPR Integration, Advanced Notifications]
                    |
[Phase 56-60: Game Discovery, Marketplace, Tenant Controls, Analytics, Scheduling]
                    |
Phase 61 -- PWA / Mobile Experience
`

### Dependency Matrix

| Phase | Depends On                                          | Depended On By          |
|-------|-----------------------------------------------------|-------------------------|
| 31    | Existing Identity, OrganizationId                  | 32-61 (all)             |
| 32    | 31, existing Court model                            | 33-61 (all)             |
| 33    | 32, existing Court model                            | 34, 35, 36, 37, 38, 39  |
| 34    | 33, 32                                              | 36, 38, 45, 52, 53      |
| 35    | 33, 34                                              | --                      |
| 36    | 33, 34, existing email service                      | 34 (promotion), 55      |
| 37    | 33, 32, existing booking                            | 61                      |
| 38    | 33, 34, 32                                          | 53, 59                  |
| 39    | 33, 34, 32, existing Court                          | 40                      |
| 40    | 39, 32                                              | 41                      |
| 41    | 40 or 44, 32                                        | 42, 43, 44, 51, 54      |
| 42    | 41                                                  | 43, 44                  |
| 43    | 41, 42, 32                                          | --                      |
| 44    | 32, 41, 42                                          | 45, 46, 47, 48, 57      |
| 45    | 44, 34, GCash payment                               | 46                      |
| 46    | 44, 45, 41                                          | 47                      |
| 47    | 46, 44, existing Courts                             | 48                      |
| 48    | 44, 41, 42, 32                                      | 59                      |
| 49    | 32                                                  | --                      |
| 50    | 32, 33                                              | 51                      |
| 51    | 50, 41                                              | --                      |
| 52    | 32, 38, 34                                          | --                      |
| 53    | 32, 41, 34, 38                                      | --                      |
| 54    | 41, 32, external DUPR API                           | --                      |
| 55    | 36, 32                                              | 61                      |
| 56    | 31, 33, 44, 57                                      | --                      |
| 57    | 31, 56                                              | --                      |
| 58    | 32-57                                               | 57                      |
| 59    | 38, 41, 44, existing booking/payment                | --                      |
| 60    | 40, 47, 48                                          | --                      |
| 61    | 36, 38, 37                                          | --                      |

---

## Existing System Integration Points

| Existing System                           | Affected Future Phases      | Integration Notes                              |
|-------------------------------------------|-----------------------------|------------------------------------------------|
| ASP.NET Core Identity                     | 31 (Customer Registration)  | Extend with Customer/Player role; preserve admin roles |
| Court model                               | 33, 39, 40, 46, 47, 60      | Read-only reference; do not modify schema      |
| CourtTimeSlot model                       | 33, 35, 47                  | Conceptual alignment; do not modify            |
| Booking model                             | 37, 38, 47                  | Activity-to-court does not create Bookings     |
| Payment + OrganizationPaymentSettings     | 34, 45                      | Extend existing GCash flow; do not duplicate   |
| BookingEmailService                       | 36, 55                      | Extend; do not build parallel email engine     |
| OrganizationId isolation                  | All (31-61)                 | Mandatory; every new entity must carry this    |
| SubscriptionPlan                          | 58                          | Feature flags may integrate with plan tiers    |
| Supabase Storage                          | 32 (profile photo), 38 (QR) | Extend existing bucket structure               |
| Anonymous booking flow                    | 31                          | Must be preserved alongside authenticated flow |

---

*This document was added to the Punit Bola project as a long-term community and competitive
feature planning roadmap. All phases herein are PLANNED / FUTURE. No application code was
modified as part of creating this document.*
