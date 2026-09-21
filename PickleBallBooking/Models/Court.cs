using System.ComponentModel.DataAnnotations;

namespace PickleBallBooking.Models;

public class Court
{
    public int Id { get; set; }

    public int OrganizationId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Supabase Storage object path for the court image, e.g.
    /// "organizations/1/courts/3/main.jpg".
    /// Null means no image has been uploaded for this court.
    /// The full public URL is derived by the storage service at runtime.
    /// </summary>
    [MaxLength(500)]
    public string? ImagePath { get; set; }

    public CourtStatus Status { get; set; } = CourtStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
