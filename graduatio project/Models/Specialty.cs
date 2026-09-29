namespace graduatio_project.Models
{
    public class Specialty
    {
        public int SpecialtyId { get; set; }

        public string NameArabic { get; set; } = string.Empty;

        public string? NameEnglish { get; set; }

        public string? DescriptionArabic { get; set; }

        public string? DescriptionEnglish { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
