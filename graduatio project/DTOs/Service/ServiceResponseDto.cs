namespace graduatio_project.DTOs.Service
{
    public class ServiceResponseDto
    {
        public int ServiceId { get; set; }
        public int CenterId { get; set; }
        public int SpecialtyId { get; set; }
        public string SpecialtyNameArabic { get; set; } = string.Empty;
        public string? SpecialtyNameEnglish { get; set; }
        public string NameArabic { get; set; } = string.Empty;
        public string? NameEnglish { get; set; }
        public string? DescriptionArabic { get; set; }
        public string? DescriptionEnglish { get; set; }
        public decimal Price { get; set; }
        public int DurationMinutes { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
