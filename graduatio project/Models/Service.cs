namespace graduatio_project.Models
{
    public class Service
    {
        public string NameArabic { get; set; } = string.Empty;

        public string? NameEnglish { get; set; }

        public string? DescriptionArabic { get; set; }

        public string? DescriptionEnglish { get; set; }

        public decimal Price { get; set; }

        public int DurationMinutes { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Center Center { get; set; } = null!;

        public Specialty Specialty { get; set; } = null!;
    }
}
