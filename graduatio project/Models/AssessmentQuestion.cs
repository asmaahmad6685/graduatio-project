namespace graduatio_project.Models
{
    public class AssessmentQuestion
    {
        public int QuestionId { get; set; }

        public string QuestionArabic { get; set; } = string.Empty;

        public string? QuestionEnglish { get; set; }

        public int DisplayOrder { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
