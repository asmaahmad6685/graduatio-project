using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace graduatio_project.DTOs.Schedules;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ScheduleWindowRequestDto : IValidatableObject
{
    [Required]
    [EnumDataType(typeof(DayOfWeek))]
    public DayOfWeek? DayOfWeek { get; set; }

    [Required]
    public TimeOnly? StartTime { get; set; }

    [Required]
    public TimeOnly? EndTime { get; set; }

    public bool IsAvailable { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartTime.HasValue && EndTime.HasValue && StartTime.Value >= EndTime.Value)
            yield return new ValidationResult("StartTime must be earlier than EndTime; overnight windows are not supported.",
                [nameof(StartTime), nameof(EndTime)]);
    }
}
