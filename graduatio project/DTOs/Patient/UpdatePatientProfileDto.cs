using System.ComponentModel.DataAnnotations;

namespace graduatio_project.DTOs.Patient
{
    public class UpdatePatientProfileDto : IValidatableObject
    {
        [Required]
        [MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [MaxLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string PhoneNumber { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? Gender { get; set; }

        [MaxLength(500)]
        public string? ProfileImageUrl { get; set; }

        public DateOnly? DateOfBirth { get; set; }

        [MaxLength(100)]
        public string? City { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // Use UTC consistently with the project's existing date/time handling.
            if (DateOfBirth.HasValue && DateOfBirth.Value > DateOnly.FromDateTime(DateTime.UtcNow))
            {
                yield return new ValidationResult(
                    "Date of birth cannot be in the future.",
                    new[] { nameof(DateOfBirth) });
            }
        }
    }
}
