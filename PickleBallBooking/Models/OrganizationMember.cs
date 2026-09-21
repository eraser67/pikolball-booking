namespace PickleBallBooking.Models;

public class OrganizationMember
{
    public int Id { get; set; }

    public int OrganizationId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public OrganizationRole Role { get; set; } = OrganizationRole.OrganizationStaff;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
