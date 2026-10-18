using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace graduatio_project.DTOs.Appointments;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateAppointmentDto : IValidatableObject
{
    private string? _patientNotes;

    [Required, Range(1, int.MaxValue)]
    public int? CenterId { get; set; }

    [Required, Range(1, int.MaxValue)]
    public int? ServiceId { get; set; }

    [Required, Range(1, int.MaxValue)]
    public int? TherapistId { get; set; }

    [Required]
    public DateTime? StartDateTime { get; set; }

    [StringLength(1000)]
    public string? PatientNotes
    {
        get => _patientNotes;
        set => _patientNotes = value?.Trim();
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartDateTime.HasValue && StartDateTime.Value.Kind != DateTimeKind.Unspecified)
            yield return new ValidationResult(
                "StartDateTime must be a local Asia/Hebron date and time without a UTC suffix or offset.",
                [nameof(StartDateTime)]);
    }
}
