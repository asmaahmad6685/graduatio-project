namespace graduatio_project.Models
{
    public class AssessmentAnswer
    {
        public int AssessmentAnswerId { get; set; }

        public int AssessmentId { get; set; }

        public int QuestionId { get; set; }

        public int? OptionId { get; set; }

        public Assessment Assessment { get; set; } = null!;

        public AssessmentQuestion Question { get; set; } = null!;

        public AssessmentOption? Option { get; set; }
    }
}
