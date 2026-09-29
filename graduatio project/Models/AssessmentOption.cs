namespace graduatio_project.Models
{
    public class AssessmentOption
    {
        public int OptionId { get; set; }

        public int QuestionId { get; set; }

        public string TextArabic { get; set; } = string.Empty;

        public string? TextEnglish { get; set; }

        public int? SpecialtyId { get; set; }

        public int? Score { get; set; }

        public AssessmentQuestion Question { get; set; } = null!;

        public Specialty? Specialty { get; set; }
    }
}
