using graduatio_project.DTOs.Assessment;

namespace graduatio_project.Services;

public static class AssessmentCatalog
{
    public const int MaxOtherDescriptionLength = 500;
    public const string Disclaimer = "This assessment is for guidance only and does not provide a medical diagnosis or replace evaluation by a qualified healthcare professional.";

    // API codes are independent of SQL identity values. Fixed bilingual text maps
    // these definitions onto the existing schema without additional columns.
    public static IReadOnlyList<AssessmentQuestionDefinitionDto> Questions { get; } =
        Array.AsReadOnly<AssessmentQuestionDefinitionDto>(
        [
            new("PainLocation", "Pain Location", "مكان الألم", "singleSelect", 1, Options(
                new("Neck", "Neck", "الرقبة"),
                new("Shoulder", "Shoulder", "الكتف"),
                new("UpperBack", "Upper back", "أعلى الظهر"),
                new("LowerBack", "Lower back", "أسفل الظهر"),
                new("ArmHand", "Arm / hand", "الذراع / اليد"),
                new("Hip", "Hip", "الورك"),
                new("Knee", "Knee", "الركبة"),
                new("LegFoot", "Leg / foot", "الساق / القدم"),
                new("MultipleAreas", "Multiple areas", "مناطق متعددة"))),
            new("PainSeverity", "Pain Severity", "شدة الألم", "integer", 2, Options(), 0, 10),
            new("Duration", "Duration", "مدة الألم", "singleSelect", 3, Options(
                new("Today", "Today", "اليوم"),
                new("TwoToSevenDays", "Two to seven days", "من يومين إلى سبعة أيام"),
                new("OneToFourWeeks", "One to four weeks", "من أسبوع إلى أربعة أسابيع"),
                new("OneToThreeMonths", "One to three months", "من شهر إلى ثلاثة أشهر"),
                new("MoreThanThreeMonths", "More than three months", "أكثر من ثلاثة أشهر"))),
            new("PainDescription", "Pain Description", "وصف الألم", "multiSelect", 4, Options(
                new("Aching", "Aching", "ألم موجع"),
                new("Stiffness", "Stiffness", "تيبس"),
                new("Sharp", "Sharp", "ألم حاد"),
                new("Burning", "Burning", "حرقان"),
                new("NumbnessTingling", "Numbness / tingling", "خدر / وخز"),
                new("PainWithMovement", "Pain with movement", "ألم مع الحركة"),
                new("Other", "Other", "أخرى"))),
            new("PreviousInjurySurgery", "Previous Injury / Surgery", "إصابة / جراحة سابقة", "singleSelect", 5, Options(
                new("None", "None", "لا يوجد"),
                new("PreviousInjury", "Previous injury", "إصابة سابقة"),
                new("PreviousSurgery", "Previous surgery", "جراحة سابقة"),
                new("RecentInjury", "Recent injury", "إصابة حديثة")))
        ]);

    public static AssessmentQuestionDefinitionDto GetQuestion(string code) =>
        Questions.Single(question => question.Code == code);

    public static bool IsValidOption(string questionCode, string? optionCode) =>
        GetQuestion(questionCode).Options.Any(option => option.Code == optionCode);

    private static IReadOnlyList<AssessmentOptionDefinitionDto> Options(params AssessmentOptionDefinitionDto[] options) =>
        Array.AsReadOnly(options);
}
