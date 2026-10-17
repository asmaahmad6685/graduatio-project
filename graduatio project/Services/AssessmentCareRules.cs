using graduatio_project.DTOs.Assessment;

namespace graduatio_project.Services;

public static class AssessmentCareRules
{
    // Product guidance categories, not disease labels or a validated triage tool.
    // Precedence is intentional: review takes priority over regional care.
    public static SuggestedCareDto Calculate(AssessmentSubmissionDto answers)
    {
        var severity = answers.PainSeverity!.Value;
        var descriptions = answers.PainDescriptions!;
        var context = DescribeAnswers(answers);

        if (severity >= 7 || answers.PreviousInjurySurgery == "RecentInjury" ||
            descriptions.Contains("NumbnessTingling", StringComparer.Ordinal))
            return new("ProfessionalReview", "Healthcare professional review before physiotherapy",
                "تقييم مختص رعاية صحية قبل العلاج الطبيعي",
                context + " Severe pain, a recent injury, or numbness/tingling warrants individual professional evaluation before choosing physiotherapy care.",
                "Arrange a qualified healthcare professional evaluation. These five questions cannot determine urgency; seek prompt medical help for sudden, worsening, or concerning symptoms.");

        if (severity == 0)
            return new("GeneralPhysiotherapyGuidance", "General physiotherapy guidance",
                "إرشاد عام للعلاج الطبيعي",
                context + " No pain is reported on the severity scale; general physiotherapy guidance may help address any remaining movement concerns.",
                "Discuss persistent stiffness, movement concerns, or other symptoms with a qualified professional. A zero pain score does not rule out a health problem.");

        if (answers.PreviousInjurySurgery == "PreviousSurgery")
            return new("PostSurgicalRehabilitation", "Assessment for rehabilitation after previous surgery",
                "تقييم التأهيل بعد جراحة سابقة",
                context + " A previous surgery makes an individual rehabilitation assessment appropriate before selecting exercises or treatment.",
                "Discuss whether the previous surgery is relevant with your clinician or physiotherapist, and follow any existing surgical restrictions.");

        if (answers.Duration == "MoreThanThreeMonths")
            return new("PersistentPainPhysiotherapy", "Physiotherapy assessment for persistent pain",
                "تقييم العلاج الطبيعي للألم المستمر",
                context + " Pain reported for more than three months suggests discussing a tailored physiotherapy care plan.",
                "Arrange an individual physiotherapy evaluation and discuss how the symptoms affect daily activities.");

        var (code, english, arabic) = answers.PainLocation switch
        {
            "Neck" or "UpperBack" or "LowerBack" =>
                ("SpinalPhysiotherapy", "Neck and back physiotherapy assessment", "تقييم العلاج الطبيعي للرقبة والظهر"),
            "Shoulder" or "ArmHand" =>
                ("UpperLimbPhysiotherapy", "Upper limb physiotherapy assessment", "تقييم العلاج الطبيعي للطرف العلوي"),
            "Hip" or "Knee" or "LegFoot" =>
                ("LowerLimbPhysiotherapy", "Lower limb physiotherapy assessment", "تقييم العلاج الطبيعي للطرف السفلي"),
            _ => ("GeneralPhysiotherapy", "General musculoskeletal physiotherapy assessment", "تقييم عام للعلاج الطبيعي العضلي الهيكلي")
        };

        var followUp = answers.Duration == "OneToThreeMonths"
            ? "Arrange a physiotherapy evaluation because symptoms have persisted for one to three months."
            : "Discuss an appropriate care plan with a qualified physiotherapist if symptoms persist or affect daily activities.";
        if (answers.PreviousInjurySurgery == "PreviousInjury")
            followUp += " Tell the physiotherapist about the previous injury.";
        if (descriptions.Contains("Other", StringComparer.Ordinal))
            followUp += " Discuss the additional symptom description during that evaluation.";

        return new(code, english, arabic, context + " The reported location guides the suggested physiotherapy care category.", followUp);
    }

    public static string InterpretSeverity(int severity) => severity switch
    {
        0 => "NoPain",
        >= 1 and <= 3 => "Mild",
        >= 4 and <= 6 => "Moderate",
        >= 7 and <= 10 => "Severe",
        _ => throw new ArgumentOutOfRangeException(nameof(severity))
    };

    // Only explicitly recognized catalog names can set the existing FK.
    // Missing/ambiguous entries leave SuggestedSpecialtyId null. No specialty seeds.
    public static IReadOnlyList<string> GetSpecialtyNames(string careCode) => careCode switch
    {
        "PostSurgicalRehabilitation" => ["Post-Surgical Rehabilitation", "Postoperative Rehabilitation", "التأهيل بعد الجراحة"],
        "PersistentPainPhysiotherapy" => ["Persistent Pain Physiotherapy", "Pain Management Physiotherapy", "العلاج الطبيعي للألم المستمر"],
        "SpinalPhysiotherapy" => ["Spinal Physiotherapy", "Neck and Back Physiotherapy", "العلاج الطبيعي للرقبة والظهر"],
        "UpperLimbPhysiotherapy" => ["Upper Limb Physiotherapy", "العلاج الطبيعي للطرف العلوي"],
        "LowerLimbPhysiotherapy" => ["Lower Limb Physiotherapy", "العلاج الطبيعي للطرف السفلي"],
        "GeneralPhysiotherapy" => ["Musculoskeletal Physiotherapy", "Orthopedic Physiotherapy", "العلاج الطبيعي العضلي الهيكلي"],
        _ => []
    };

    private static string DescribeAnswers(AssessmentSubmissionDto answers)
    {
        static string Label(string question, string code) =>
            AssessmentCatalog.GetQuestion(question).Options.Single(option => option.Code == code).TextEnglish;

        var symptoms = AssessmentCatalog.GetQuestion("PainDescription").Options
            .Where(option => answers.PainDescriptions!.Contains(option.Code, StringComparer.Ordinal))
            .Select(option => option.TextEnglish.ToLowerInvariant());
        return $"Location: {Label("PainLocation", answers.PainLocation!)}. " +
            $"Severity: {answers.PainSeverity}/10 ({InterpretSeverity(answers.PainSeverity!.Value)}). " +
            $"Duration: {Label("Duration", answers.Duration!)}. " +
            $"Descriptions: {string.Join(", ", symptoms)}. " +
            $"Previous injury / surgery: {Label("PreviousInjurySurgery", answers.PreviousInjurySurgery!)}.";
    }
}
