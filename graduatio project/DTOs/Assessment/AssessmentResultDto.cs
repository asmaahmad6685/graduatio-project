using graduatio_project.Services;

namespace graduatio_project.DTOs.Assessment;

public record AssessmentResultDto
{
    public int AssessmentId { get; init; }
    public DateTime CreatedAt { get; init; }
    public bool IsPainAssessment { get; init; }
    public AssessmentSubmissionDto? Answers { get; init; }
    public string? SeverityInterpretation { get; init; }
    public SuggestedCareDto? SuggestedCare { get; init; }
    public int? SuggestedSpecialtyId { get; init; }
    public string? SuggestedSpecialtyName { get; init; }
    public string Disclaimer { get; init; } = AssessmentCatalog.Disclaimer;
}

public record SuggestedCareDto(
    string Code,
    string NameEnglish,
    string NameArabic,
    string Explanation,
    string FollowUp);
