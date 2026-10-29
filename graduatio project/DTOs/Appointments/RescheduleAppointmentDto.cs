using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace graduatio_project.DTOs.Appointments;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RescheduleAppointmentDto : IValidatableObject
{
    [Required, Range(1, int.MaxValue)]
    public int? TherapistId { get; set; }

    [Required]
    public DateTime? StartDateTime { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartDateTime.HasValue && StartDateTime.Value.Kind != DateTimeKind.Unspecified)
            yield return new ValidationResult(
                "StartDateTime must be a local Asia/Hebron date and time without a UTC suffix or offset.",
                [nameof(StartDateTime)]);
    }
}
