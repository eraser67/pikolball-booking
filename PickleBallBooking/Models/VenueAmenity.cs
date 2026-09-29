namespace PickleBallBooking.Models;

/// <summary>
/// Represents a single venue amenity with a Bootstrap Icon and a display label.
/// The Key is what gets stored in Organization.AmenitiesKeys (comma-separated).
/// </summary>
public sealed record VenueAmenity(string Key, string Icon, string Label, string Group);

/// <summary>
/// Static registry of all supported venue amenities.
/// </summary>
public static class VenueAmenities
{
    public static readonly IReadOnlyList<VenueAmenity> All = new[]
    {
        // Facilities
        new VenueAmenity("Parking",        "bi-p-square-fill",       "Free Parking",          "Facilities"),
        new VenueAmenity("PaidParking",    "bi-p-circle",            "Paid Parking",           "Facilities"),
        new VenueAmenity("Restrooms",      "bi-door-open",           "Restrooms",              "Facilities"),
        new VenueAmenity("Shower",         "bi-droplet-half",        "Shower Room",            "Facilities"),
        new VenueAmenity("LockerRoom",     "bi-lock",                "Locker Room",            "Facilities"),
        new VenueAmenity("SpectatorArea",  "bi-binoculars",          "Spectator Area",         "Facilities"),
        new VenueAmenity("AirCon",         "bi-thermometer-snow",    "Air Conditioned",        "Facilities"),
        new VenueAmenity("Covered",        "bi-house-door",          "Covered Courts",         "Facilities"),
        new VenueAmenity("Outdoor",        "bi-sun",                 "Outdoor Courts",         "Facilities"),
        // Services
        new VenueAmenity("EquipmentRental","bi-tools",               "Equipment Rental",       "Services"),
        new VenueAmenity("Coaching",       "bi-person-workspace",    "Coaching Available",     "Services"),
        new VenueAmenity("BallMachine",    "bi-robot",               "Ball Machine",           "Services"),
        new VenueAmenity("Stringing",      "bi-wrench",              "Racquet Stringing",      "Services"),
        new VenueAmenity("ProShop",        "bi-bag",                 "Pro Shop",               "Services"),
        // Food & Drinks
        new VenueAmenity("WaterStation",   "bi-cup",                 "Water Station",          "Food & Drinks"),
        new VenueAmenity("Snacks",         "bi-cup-hot",             "Snacks Available",       "Food & Drinks"),
        new VenueAmenity("Canteen",        "bi-shop",                "Canteen / Cafeteria",    "Food & Drinks"),
        // Connectivity & Safety
        new VenueAmenity("WiFi",           "bi-wifi",                "Free Wi-Fi",             "Connectivity & Safety"),
        new VenueAmenity("CCTV",           "bi-camera-video",        "CCTV Security",          "Connectivity & Safety"),
        new VenueAmenity("FirstAid",       "bi-heart-pulse",         "First Aid Kit",          "Connectivity & Safety"),
        new VenueAmenity("Security",       "bi-shield-check",        "On-Site Security",       "Connectivity & Safety"),
        // Family
        new VenueAmenity("KidsArea",       "bi-balloon",             "Kids Area",              "Family"),
        new VenueAmenity("Accessible",     "bi-person-wheelchair",   "Wheelchair Accessible",  "Family"),
        new VenueAmenity("PetsAllowed",    "bi-piggy-bank",          "Pets Allowed",           "Family"),
    };

    public static IEnumerable<IGrouping<string, VenueAmenity>> Grouped
        => All.GroupBy(a => a.Group);

    public static IReadOnlyList<VenueAmenity> Parse(string? keys)
    {
        if (string.IsNullOrWhiteSpace(keys)) return Array.Empty<VenueAmenity>();
        var keySet = keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return All.Where(a => keySet.Contains(a.Key)).ToList();
    }

    public static string Serialize(IEnumerable<string>? keys)
        => keys is null ? string.Empty : string.Join(",", keys.Where(k => !string.IsNullOrWhiteSpace(k)));
}
