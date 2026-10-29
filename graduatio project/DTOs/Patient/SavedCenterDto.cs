namespace graduatio_project.DTOs.Patient
{
    public class SavedCenterDto
    {
        public int CenterId { get; set; }

        public string NameArabic { get; set; } = string.Empty;

        public string? NameEnglish { get; set; }

        public string? DescriptionArabic { get; set; }

        public string? DescriptionEnglish { get; set; }

        public string City { get; set; } = string.Empty;

        public string Address { get; set; } = string.Empty;

        public string? ProfileImageUrl { get; set; }

        public double? Latitude { get; set; }

        public double? Longitude { get; set; }

        public string Status { get; set; } = string.Empty;

        public bool IsSaved { get; set; }

        public DateTime SavedAt { get; set; }
    }
}
