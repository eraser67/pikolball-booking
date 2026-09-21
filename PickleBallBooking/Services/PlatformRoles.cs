namespace PickleBallBooking.Services;

/// <summary>
/// Phase 23: well-known ASP.NET Core Identity role names used for PLATFORM-level
/// authorization.
///
/// This is NOT a second organization role system: the domain-level roles
/// (<see cref="Models.OrganizationRole"/>) are unchanged and continue to live on
/// <see cref="Models.OrganizationMember"/>. Platform administration is a property of
/// the Identity account itself (can this user manage the whole platform?), which is
/// exactly what Identity roles are for, and matches the roadmap's PlatformAdmin role.
/// </summary>
public static class PlatformRoles
{
    /// <summary>Identity role for platform administrators (manage all organizations).</summary>
    public const string PlatformAdmin = "PlatformAdmin";

    /// <summary>Authorization policy name that requires the platform-admin role.</summary>
    public const string PlatformAdminPolicy = "PlatformAdminOnly";
}
