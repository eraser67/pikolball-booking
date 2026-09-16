# Court Availability Display Enhancement - 12-Hour Time Format with Court Indicators

## Summary of Changes

Enhanced the Available Time Slots section on the Availability page to display:
- **12-hour time format with AM/PM** (e.g., "06:00 AM - 07:00 AM")
- **Which courts are available** for each time slot
- **Status indicators** showing available vs. fully booked courts

## Files Modified

### 1. `PickleBallBooking/Pages/Availability/Index.cshtml.cs` (PageModel)

**New Classes Added:**
- `TimeSlotAvailability` - Represents a time slot with per-court availability details
  - `StartTime` and `EndTime` (TimeSpan)
  - `FormattedLabel` - 12-hour time format with AM/PM
  - `Price` (decimal)
  - `AvailableCourts` - List of courts available for this slot
  - `BookedCourts` - List of courts booked for this slot
  - `HasAvailableSlots` - Boolean indicating if any courts are available
  - `CourtStatusText` - Human-readable status (e.g., "2 courts available")

**Functionality Added:**
- Replaced `RangeOptions` (CalendarRangeOption list) with `TimeSlotAvailabilities` (TimeSlotAvailability list)
- Added `Format12HourTime(TimeSpan start, TimeSpan end)` method
  - Converts 24-hour format to 12-hour format with AM/PM
  - Example: 06:00 → 06:00 AM, 18:00 → 06:00 PM
- Enhanced `OnGetAsync()` to populate per-court availability for each time slot
  - For each time slot, checks availability for every court
  - Separates courts into AvailableCourts and BookedCourts lists
- Added `GetAvailableCourtsList(List<Court> courts)` helper method

### 2. `PickleBallBooking/Pages/Availability/Index.cshtml` (Razor View)

**UI Enhancements:**
- Time slots now display in 12-hour format with AM/PM indicators
  - Example: "06:00 AM - 07:00 AM" instead of "06:00 - 07:00"
- Each time slot card shows:
  - **Time slot in 12-hour format** with AM/PM
  - **Court availability status badge** (green "Available" or red "Fully Booked")
  - **Available courts count** as text (e.g., "2 courts available")
  - **List of available/booked courts** with color-coded badges
	- Available courts: Info badge (light blue)
	- Booked courts: Danger badge (red)
  - **Price** displayed prominently for available slots
- Clickable links on available time slots (green cards)
- Read-only display on fully booked time slots (gray cards)

## User Experience Improvements

### Before
- Time slots showed only 24-hour format (06:00, 18:00)
- No indication of which specific courts were available
- Users had to guess whether courts were available or booked

### After
- **Clear 12-hour format with AM/PM** - More familiar to users
  - Morning: 06:00 AM, 07:00 AM, 08:00 AM, 09:00 AM
  - Afternoon: 12:00 PM, 01:00 PM, 02:00 PM
  - Evening: 05:00 PM, 06:00 PM, 07:00 PM, 08:00 PM

- **Court-level transparency**
  - Users immediately see which specific courts are available
  - Example badge display: "Court 1 - Indoor Premium" and "Court 3 - Outdoor Covered"

- **Color-coded court status**
  - Available courts: Light blue badge ✓
  - Booked courts: Red badge ✗

## Data Structure

### TimeSlotAvailability (New Model)
```csharp
public class TimeSlotAvailability
{
	public TimeSpan StartTime { get; set; }
	public TimeSpan EndTime { get; set; }
	public string Label { get; set; }
	public string FormattedLabel { get; set; }  // "06:00 AM - 07:00 AM"
	public decimal? Price { get; set; }
	public List<Court> AvailableCourts { get; set; }
	public List<Court> BookedCourts { get; set; }
	public bool HasAvailableSlots { get; }
	public string CourtStatusText { get; }  // "2 courts available"
}
```

## Tests Updated

### Modified: `PickleBallBooking.Tests/Pages/AvailabilityIndexModelTests.cs`
- Updated all test assertions to use `TimeSlotAvailabilities` instead of `RangeOptions`
- All 4 availability tests passing:
  - ✓ OnGetAsync_DefaultsToToday_WhenNoDateProvided
  - ✓ OnGetAsync_LoadsCalendarRangesAndActiveCourts
  - ✓ OnGetAsync_MarksSelectedRange_WhenProvidedViaQuery
  - ✓ OnGetAsync_MarksPastDateAsUnavailable

### Test Results
- **22/22 tests passing** (Availability + Booking page tests)
- All time slot availability calculations verified
- Court availability tracking per time slot validated

## Example Display

### Time Slot Card (Available)
```
06:00 AM - 07:00 AM              [Available - 2 courts available]
Available Courts:
  [Court 1 - Indoor Premium]
  [Court 3 - Outdoor Covered]
₱250.00
```

### Time Slot Card (Fully Booked)
```
05:00 PM - 06:00 PM              [Fully Booked]
All Courts Booked:
  [Court 1 - Indoor Premium]
  [Court 2 - Indoor Standard]
  [Court 4 - Outdoor Open]
```

## Build Status
✅ **Build Successful** - Project compiles without errors or warnings
✅ **Tests Passing** - 22/22 availability and booking tests pass
✅ **Backward Compatible** - Existing booking functionality unchanged

## Benefits
1. **User-Friendly Time Format** - 12-hour AM/PM is more intuitive for users
2. **Transparency** - Explicit court availability eliminates guesswork
3. **Better UX** - Color coding and badges provide immediate visual feedback
4. **Mobile-Friendly** - Responsive layout works on all screen sizes
5. **Accessible** - Screen readers can access all court names and availability info
