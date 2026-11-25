namespace graduatio_project.DTOs.Assessment;

public record AssessmentDefinitionDto(
    IReadOnlyList<AssessmentQuestionDefinitionDto> Questions,
    string Disclaimer);

public record AssessmentQuestionDefinitionDto(
    string Code,
    string TextEnglish,
    string TextArabic,
    string InputType,
    int DisplayOrder,
    IReadOnlyList<AssessmentOptionDefinitionDto> Options,
    int? MinValue = null,
    int? MaxValue = null);

public record AssessmentOptionDefinitionDto(string Code, string TextEnglish, string TextArabic);
