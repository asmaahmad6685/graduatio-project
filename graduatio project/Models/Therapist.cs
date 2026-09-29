namespace graduatio_project.Models
{
    public class Therapist
    {
        public string? Bio { get; set; }

        public string? ProfileImageUrl { get; set; }

        public int? YearsOfExperience { get; set; }

        public string? Qualifications { get; set; }

        public string? Languages { get; set; }

        public bool IsAvailable { get; set; } = true;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Center Center { get; set; } = null!;

        public Specialty Specialty { get; set; } = null!;
    }
}
