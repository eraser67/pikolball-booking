# Available Time Slots Display - Visual Guide

## Time Format Conversion
The availability page now displays times in **12-hour format with AM/PM indicators** instead of 24-hour format.

### Examples:
```
24-hour Format  →  12-hour Format
06:00          →  06:00 AM
07:00          →  07:00 AM
08:00          →  08:00 AM
09:00          →  09:00 AM
10:00          →  10:00 AM
11:00          →  11:00 AM
12:00          →  12:00 PM
13:00          →  01:00 PM
14:00          →  02:00 PM
17:00          →  05:00 PM
18:00          →  06:00 PM
19:00          →  07:00 PM
20:00          →  08:00 PM
21:00          →  09:00 PM
```

## Court Availability Indicators

Each time slot card now shows which courts are available for booking:

### Example 1: Available Courts
```
┌─────────────────────────────────────────────────────────────┐
│  06:00 AM - 07:00 AM          ✓ Available - 2 courts available   │
├─────────────────────────────────────────────────────────────┤
│  Available Courts:                                          │
│  • Court 1 - Indoor Premium                                │
│  • Court 3 - Outdoor Covered                               │
│                                                             │
│                                  ₱250.00                    │
└─────────────────────────────────────────────────────────────┘
```

### Example 2: Partially Available
```
┌─────────────────────────────────────────────────────────────┐
│  05:00 PM - 06:00 PM          ✓ Available - 1 court available    │
├─────────────────────────────────────────────────────────────┤
│  Available Courts:                                          │
│  • Court 4 - Outdoor Open                                  │
│                                                             │
│  Booked Courts:                                             │
│  • Court 1 - Indoor Premium                                │
│  • Court 2 - Indoor Standard                               │
│  • Court 3 - Outdoor Covered                               │
│                                                             │
│                                  ₱350.00                    │
└─────────────────────────────────────────────────────────────┘
```

### Example 3: Fully Booked
```
┌─────────────────────────────────────────────────────────────┐
│  12:00 PM - 01:00 PM          ✗ Fully Booked                  │
├─────────────────────────────────────────────────────────────┤
│  All Courts Booked:                                         │
│  • Court 1 - Indoor Premium                                │
│  • Court 2 - Indoor Standard                               │
│  • Court 3 - Outdoor Covered                               │
│  • Court 4 - Outdoor Open                                  │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

## Page Layout

### Full Availability Page Structure:
```
┌──────────────────────────────────────────────────────────────┐
│  "Court Availability"                                        │
│  Description: Choose a date to review the current booking   │
│  status and available time ranges...                        │
├──────────────────────────────────────────────────────────────┤
│  [Date Picker: ___________]  [Go] [Previous] [Next] [Today]   │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  "Court Availability Status"                                │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐      │
│  │ Court 1      │  │ Court 2      │  │ Court 3      │      │
│  │ Indoor       │  │ Indoor       │  │ Outdoor      │      │
│  │ Premium      │  │ Standard     │  │ Covered      │      │
│  │              │  │              │  │              │      │
│  │ ✓ Available  │  │ ✓ Available  │  │ ✓ Available  │      │
│  │ 6/9 slots    │  │ 8/9 slots    │  │ 7/9 slots    │      │
│  │ [████░░░░░]  │  │ [████████░░] │  │ [██████░░░░] │      │
│  └──────────────┘  └──────────────┘  └──────────────┘      │
│                                                              │
├──────────────────────────────────────────────────────────────┤
│  "Available Time Slots for Monday, January 6, 2025"          │
│  [MON]  [TUE]  [WED]  [THU]  [FRI]  [SAT]  [SUN]            │
│   6     7      8      9      10     11     12               │
│                                                              │
│  Time Slot Cards Grid (Responsive):                         │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐      │
│  │ 06:00 AM    │  │ 07:00 AM    │  │ 08:00 AM    │      │
│  │ - 07:00 AM  │  │ - 08:00 AM  │  │ - 09:00 AM  │      │
│  │             │  │             │  │             │      │
│  │ ✓ Available │  │ ✓ Available │  │ ✗ Fully     │      │
│  │ 2 courts    │  │ 3 courts    │  │ Booked      │      │
│  │             │  │             │  │             │      │
│  │ Courts:     │  │ Courts:     │  │ Booked:     │      │
│  │ •Indoor Pr. │  │ •Indoor Pr. │  │ •Outdoor Op.│      │
│  │ •Outdoor C. │  │ •Outdoor C. │  │             │      │
│  │             │  │             │  │             │      │
│  │ ₱250.00     │  │ ₱250.00     │  │             │      │
│  └──────────────┘  └──────────────┘  └──────────────┘      │
│                                                              │
│  [More time slot cards...]                                  │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

## Key Features

✅ **12-Hour Time Format**
- Morning: AM (06:00 AM - 12:00 PM)
- Afternoon: PM (12:00 PM - 05:00 PM)
- Evening: PM (05:00 PM - 09:00 PM)

✅ **Court-Level Transparency**
- See exactly which courts are available
- See which courts are booked
- Court names clearly indicate type (Indoor, Outdoor, etc.)

✅ **Visual Status Indicators**
- Green checkmark badge: Available slots
- Red X badge: Fully booked
- Info badges: Show individual court names
- Progress bar: Overall court availability percentage

✅ **Responsive Design**
- Mobile: 1 column layout
- Tablet: 2 column layout
- Desktop: 3 column layout
- Cards automatically stack and resize

✅ **Interactive Elements**
- Click on available time slots to proceed with booking
- Grayed out fully booked time slots
- Date navigation buttons for browsing different days
