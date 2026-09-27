namespace PickleBallBooking.Models;

/// <summary>
/// Phase 33: target skill level for an activity.
/// Extends PlayerSkillLevel with an Open tier that welcomes all skill levels.
/// </summary>
public enum ActivitySkillLevel
{
    Open         = 0,
    Beginner     = 1,
    Intermediate = 2,
    Advanced     = 3,
}
