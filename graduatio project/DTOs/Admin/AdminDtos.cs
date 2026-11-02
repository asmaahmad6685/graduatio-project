using System.ComponentModel.DataAnnotations;

namespace graduatio_project.DTOs.Admin
{
    // PUT /api/admin/centers/{id}/status
    // PUT /api/admin/patients/{id}/status
    // PUT /api/admin/physiotherapists/{id}/status
    public class UpdateStatusDto
    {
        [Required]
        [MaxLength(30)]
        public string Status { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Reason { get; set; }
    }

    // POST /api/admin/physiotherapists
    public class AdminCreateTherapistDto
    {
        [Required, MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required, MinLength(8)]
        public string Password { get; set; } = string.Empty;

        [Required]
        public int CenterId { get; set; }

        [Required]
        public int SpecialtyId { get; set; }

        [MaxLength(150)]
        public string? FullNameAr { get; set; }

        [MaxLength(1000)]
        public string? Bio { get; set; }

        [MaxLength(1000)]
        public string? BioAr { get; set; }

        public int? YearsOfExperience { get; set; }

        [MaxLength(1000)]
        public string? Qualifications { get; set; }

        [MaxLength(300)]
        public string? Languages { get; set; }
    }

    // POST /api/admin/appointments
    public class AdminCreateAppointmentDto
    {
        [Required]
        public int PatientId { get; set; }

        [Required]
        public int CenterId { get; set; }

        [Required]
        public int TherapistId { get; set; }

        [Required]
        public int ServiceId { get; set; }

        [Required]
        public DateTime StartDateTime { get; set; }

        [Required]
        public DateTime EndDateTime { get; set; }

        [MaxLength(1000)]
        public string? PatientNotes { get; set; }
    }

    // POST /api/admin/reviews/{id}/moderate
    public class ModerateReviewDto
    {
        [Required]
        [RegularExpression("^(?i:hide|show|remove)$",
            ErrorMessage = "Action must be hide, show, or remove.")]
        public string Action { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Reason { get; set; }
    }

    // PUT /api/admin/settings
    public class AdminSettingsDto
    {
        [MaxLength(150)]
        public string? AdminFullName { get; set; }

        [EmailAddress, MaxLength(256)]
        public string? ContactEmail { get; set; }

        [MaxLength(20)]
        public string? ContactPhone { get; set; }

        [MaxLength(500)]
        public string? SiteDescription { get; set; }

        public bool? MaintenanceMode { get; set; }
    }
}
