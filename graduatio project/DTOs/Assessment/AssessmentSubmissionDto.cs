using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using graduatio_project.Services;

namespace graduatio_project.DTOs.Assessment;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class AssessmentSubmissionDto : IValidatableObject
{
    [Required]
    public string? PainLocation { get; set; }

    [Required]
    [Range(0, 10)]
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int? PainSeverity { get; set; }

    [Required]
    public string? Duration { get; set; }

    [Required]
    [MinLength(1)]
    public List<string>? PainDescriptions { get; set; }

    [Required]
    public string? PreviousInjurySurgery { get; set; }

    public string? OtherDescription { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!AssessmentCatalog.IsValidOption("PainLocation", PainLocation))
            yield return new ValidationResult("Select one approved Pain Location code.", [nameof(PainLocation)]);

        if (!AssessmentCatalog.IsValidOption("Duration", Duration))
            yield return new ValidationResult("Select one approved Duration code.", [nameof(Duration)]);

        if (!AssessmentCatalog.IsValidOption("PreviousInjurySurgery", PreviousInjurySurgery))
            yield return new ValidationResult("Select one approved Previous Injury / Surgery code.", [nameof(PreviousInjurySurgery)]);

        if (PainDescriptions is not null)
        {
            if (PainDescriptions.Any(code => !AssessmentCatalog.IsValidOption("PainDescription", code)))
                yield return new ValidationResult("Pain Description contains an unapproved code.", [nameof(PainDescriptions)]);

            if (PainDescriptions.Distinct(StringComparer.Ordinal).Count() != PainDescriptions.Count)
                yield return new ValidationResult("Pain Description selections must not be duplicated.", [nameof(PainDescriptions)]);
        }

        if (OtherDescription is not null && PainDescriptions?.Contains("Other", StringComparer.Ordinal) != true)
            yield return new ValidationResult("OtherDescription may only be supplied when Other is selected.", [nameof(OtherDescription)]);

        if (OtherDescription?.Trim().Length > AssessmentCatalog.MaxOtherDescriptionLength)
            yield return new ValidationResult($"OtherDescription must be at most {AssessmentCatalog.MaxOtherDescriptionLength} characters after trimming.", [nameof(OtherDescription)]);
    }
}
