namespace graduatio_project.DTOs.Patient
{
    public class PatientProfileDto
    {
        public int PatientId { get; set; }

        public string FullName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string PhoneNumber { get; set; } = string.Empty;

        public string? Gender { get; set; }

        public string? ProfileImageUrl { get; set; }

        public DateOnly? DateOfBirth { get; set; }

        public string? City { get; set; }
    }
}
