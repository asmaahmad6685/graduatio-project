using System.ComponentModel.DataAnnotations;

namespace graduatio_project.DTOs.Availability;

public sealed class AvailabilityQueryDto
{
    [Required]
    [Range(1, int.MaxValue)]
    public int? ServiceId { get; set; }

    [Required]
    public DateOnly? Date { get; set; }
}
