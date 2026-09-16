# Database Reset & Court Availability Features

## Overview
This document describes the new database reset functionality and enhanced court availability display features implemented in Phase 17+ of the Pickleball Booking application.

## New Features

### 1. Database Reset with Sample Data
A new `DatabaseResetSeeder.cs` utility provides the ability to completely reset the database and reseed it with fresh sample data. This is useful for development, testing, and demos.

**Location:** `PickleBallBooking/Data/DatabaseResetSeeder.cs`

#### What Gets Reset:
- All bookings (deleted)
- All pricing rules (deleted)
- All time slots (deleted)
- All courts (deleted)

#### What Gets Seeded:
- **5 Courts** with descriptive names and statuses:
  - Court 1 - Indoor Premium (Active)
  - Court 2 - Indoor Standard (Active)
  - Court 3 - Outdoor Covered (Active)
  - Court 4 - Outdoor Open (Active)
  - Court 5 - Practice Court (Inactive)

- **9 Time Slots** covering the day:
  - Morning: 6AM-7AM, 7AM-8AM, 8AM-9AM
  - Midday: 12PM-1PM, 1PM-2PM
  - Evening: 5PM-6PM, 6PM-7PM, 7PM-8PM, 8PM-9PM

- **6 Pricing Rules** (Weekday & Weekend):
  - Morning slots: ₱250 (weekday), ₱300 (weekend)
  - Midday slots: ₱300 (weekday), ₱350 (weekend)
  - Evening slots: ₱350 (weekday), ₱400 (weekend)

- **18 Sample Bookings** with realistic data:
  - Mix of confirmed, pending, completed, and cancelled bookings
  - Spread across today, tomorrow, and future dates
  - Varied customer names and contact information
  - Realistic pricing calculations based on day type and time slot

### 2. Database Reset Endpoint (Development Only)
A secure development-only HTTP endpoint allows triggering the database reset:

**Endpoint:** `POST /dev/reset-database`

- Only available in development environment (`if (app.Environment.IsDevelopment())`)
- Returns JSON response on success or error
- Example success response:
  ```json
  {
	"message": "Database reset and reseeded successfully!"
  }
  ```

### 3. Database Reset UI (Development Only)
A user-friendly admin page for triggering the database reset:

**Location:** `PickleBallBooking/Pages/Admin/DatabaseReset.cshtml`
**Route:** `/Admin/DatabaseReset`

**Features:**
- Clear warning about destructive operation
- Lists what will be deleted and added
- Displays sample court descriptions and capabilities
- Two-confirm safety: page confirmation + JavaScript confirmation
- Real-time feedback on reset status
- Auto-redirects to availability page on success
- Error messages displayed inline

### 4. Enhanced Court Availability Display
The Availability page now shows detailed court availability statistics:

**Location:** `PickleBallBooking/Pages/Availability/Index.cshtml.cs` and `.cshtml`

**New Features:**
- **Court Availability Status Cards** showing:
  - Court name with descriptive type (Indoor Premium, Outdoor Covered, etc.)
  - Court description (features, lighting, equipment, etc.)
  - Availability badge (Available/Fully Booked)
  - Available slots count (e.g., "3/9 slots available")
  - Progress bar showing availability percentage
  - Quick status text with checkmark or X

- **Visual Indicators:**
  - Green badge when slots are available
  - Red badge when fully booked
  - Progress bar with visual percentage
  - Color-coded time slot cards

- **Responsive Layout:**
  - Mobile: Single column layout
  - Tablet: 2-column grid
  - Desktop: 3-column grid
  - All cards are same height for visual consistency

### 5. Code Changes Summary

#### New Files Created:
1. `PickleBallBooking/Data/DatabaseResetSeeder.cs` - Seeder utility
2. `PickleBallBooking/Pages/Admin/DatabaseReset.cshtml` - Reset UI page

#### Modified Files:
1. `PickleBallBooking/Program.cs` - Added development-only endpoint
2. `PickleBallBooking/Pages/Availability/Index.cshtml.cs` - Enhanced with availability calculations
3. `PickleBallBooking/Pages/Availability/Index.cshtml` - New UI with court status display

## Usage Instructions

### Triggering Database Reset

#### Option 1: Using the UI (Recommended for Development)
1. Go to `/Admin/DatabaseReset`
2. Review the sample data that will be created
3. Click "Reset Database Now"
4. Confirm the destruction warning
5. Wait for completion and auto-redirect to availability page

#### Option 2: Using cURL/Postman
```bash
curl -X POST https://localhost:5001/dev/reset-database
```

### Viewing Court Availability
1. Navigate to `/Availability`
2. Select a date using the calendar buttons or date picker
3. View court availability cards showing how many slots are available
4. See all available time slots below with pricing
5. Click any available slot to proceed with booking

## Sample Data Details

### Court Names with Availability Indicators
The court names themselves now clearly indicate the court type:
- **Indoor Premium** - Full facilities, best for serious players
- **Indoor Standard** - Good for practice, reduced amenities
- **Outdoor Covered** - Protection from elements, great for morning games
- **Outdoor Open** - Tournament-ready, space for spectators
- **Practice Court** - Smaller court, maintenance status clear

This naming convention makes it immediately obvious what type of facility each court offers.

## Important Notes

⚠️ **Development Only Features**
- The `/dev/reset-database` endpoint is only accessible in development environment
- The `/Admin/DatabaseReset` page requires admin authorization (Authorize folder for /Admin)
- In production builds, these features are completely disabled

✅ **Data Integrity**
- Foreign key constraints are respected during deletion (deletes in correct order: Bookings → Pricings → TimeSlots → Courts)
- All seeded data uses realistic, Philippine-locale test data
- Bookings reference valid courts and pricing rules

📊 **Test Coverage**
- All 27 existing tests continue to pass
- Tests cover both booking and availability functionality
- New availability calculations maintain backward compatibility

## Future Enhancements

Potential additions to consider:
- Bulk import/export of bookings
- Partial reset (reset only bookings, keep courts/pricing)
- Scheduled automatic data reset for demo environments
- Database backup before reset
- Reset history/audit log
- Custom seed data via configuration files
