using System.ComponentModel.DataAnnotations;

namespace graduatio_project.DTOs.Service
{
    public class CreateServiceDto : IValidatableObject
    {
        [Required]
        [MaxLength(200)]
        public string NameArabic { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? NameEnglish { get; set; }

        [MaxLength(1500)]
        public string? DescriptionArabic { get; set; }

        [MaxLength(1500)]
        public string? DescriptionEnglish { get; set; }

        [Required]
        [Range(typeof(decimal), "0", "99999999.99")]
        public decimal? Price { get; set; }

        [Required]
        [Range(1, 1440)]
        public int? DurationMinutes { get; set; }

        [Required]
        [Range(1, int.MaxValue)]
        public int? SpecialtyId { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Price.HasValue && decimal.Round(Price.Value, 2) != Price.Value)
            {
                yield return new ValidationResult(
                    "Price must have at most two decimal places.", new[] { nameof(Price) });
            }
        }
    }
}
