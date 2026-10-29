using graduatio_project.DTOs.Assessment;
using graduatio_project.Services;
using Xunit;

namespace PhysioGuide.Assessment.Tests;

public sealed class AssessmentCareRulesTests
{
    private static AssessmentSubmissionDto Answers(string location = "LowerBack", int severity = 5,
        string duration = "OneToFourWeeks", string injury = "None", string[]? descriptions = null) => new()
    {
        PainLocation = location,
        PainSeverity = severity,
        Duration = duration,
        PreviousInjurySurgery = injury,
        PainDescriptions = (descriptions ?? ["Aching"]).ToList()
    };

    [Theory]
    [InlineData("Neck", "SpinalPhysiotherapy")]
    [InlineData("UpperBack", "SpinalPhysiotherapy")]
    [InlineData("LowerBack", "SpinalPhysiotherapy")]
    [InlineData("Shoulder", "UpperLimbPhysiotherapy")]
    [InlineData("ArmHand", "UpperLimbPhysiotherapy")]
    [InlineData("Hip", "LowerLimbPhysiotherapy")]
    [InlineData("Knee", "LowerLimbPhysiotherapy")]
    [InlineData("LegFoot", "LowerLimbPhysiotherapy")]
    [InlineData("MultipleAreas", "GeneralPhysiotherapy")]
    public void RegionalCareRulesCoverEveryApprovedLocation(string location, string expectedCode)
    {
        var result = AssessmentCareRules.Calculate(Answers(location));
        Assert.Equal(expectedCode, result.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.NameEnglish));
        Assert.False(string.IsNullOrWhiteSpace(result.NameArabic));
    }

    [Theory]
    [InlineData(7, "MoreThanThreeMonths", "PreviousSurgery", "Aching", "ProfessionalReview")]
    [InlineData(0, "MoreThanThreeMonths", "RecentInjury", "Aching", "ProfessionalReview")]
    [InlineData(0, "MoreThanThreeMonths", "PreviousSurgery", "NumbnessTingling", "ProfessionalReview")]
    [InlineData(0, "MoreThanThreeMonths", "PreviousSurgery", "Aching", "GeneralPhysiotherapyGuidance")]
    [InlineData(5, "MoreThanThreeMonths", "PreviousSurgery", "Aching", "PostSurgicalRehabilitation")]
    [InlineData(5, "MoreThanThreeMonths", "PreviousInjury", "Aching", "PersistentPainPhysiotherapy")]
    public void PriorityRulesOverrideLowerPriorityCare(int severity, string duration,
        string injury, string description, string expectedCode)
    {
        var result = AssessmentCareRules.Calculate(Answers(severity: severity, duration: duration,
            injury: injury, descriptions: [description]));
        Assert.Equal(expectedCode, result.Code);
    }

    [Fact]
    public void IdenticalAnswersProduceIdenticalResultAndKeepAllFiveAnswersInContext()
    {
        var answers = Answers(location: "Knee", severity: 3, duration: "OneToThreeMonths",
            injury: "PreviousInjury", descriptions: ["Stiffness", "Other"]);
        answers.OtherDescription = "Additional symptom";
        var first = AssessmentCareRules.Calculate(answers);
        var second = AssessmentCareRules.Calculate(answers);
        Assert.Equal(first, second);
        Assert.Contains("Knee", first.Explanation);
        Assert.Contains("3/10", first.Explanation);
        Assert.Contains("stiffness", first.Explanation);
        Assert.Contains("other", first.Explanation);
        Assert.Contains("one to three months", first.FollowUp, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("previous injury", first.FollowUp, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("additional symptom", first.FollowUp, StringComparison.OrdinalIgnoreCase);
    }
}
