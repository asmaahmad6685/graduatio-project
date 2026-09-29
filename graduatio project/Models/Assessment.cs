namespace graduatio_project.Models
{
    public class Assessment
    {
        public int AssessmentId { get; set; }

        public int PatientId { get; set; }

        public int? SuggestedSpecialtyId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Patient Patient { get; set; } = null!;

        public Specialty? SuggestedSpecialty { get; set; }
    }
}
