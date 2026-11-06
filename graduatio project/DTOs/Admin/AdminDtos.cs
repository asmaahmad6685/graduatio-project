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

    // PUT /api/admin/patients/{id}
    public class AdminUpdatePatientDto
    {
        [Required, MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? PhoneNumber { get; set; }

        [MaxLength(100)]
        public string? City { get; set; }

        [MaxLength(20)]
        public string? Gender { get; set; }

        public DateOnly? DateOfBirth { get; set; }
    }

    // PUT /api/admin/physiotherapists/{id}
    public class AdminUpdateTherapistDto
    {
        [Required, MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? PhoneNumber { get; set; }

        public int? CenterId { get; set; }

        public int? SpecialtyId { get; set; }

        public int? YearsOfExperience { get; set; }

        [MaxLength(1000)]
        public string? Bio { get; set; }

        public bool? IsAvailable { get; set; }
    }

    // PUT /api/admin/appointments/{id}
    public class AdminUpdateAppointmentDto
    {
        public int? CenterId { get; set; }

        public int? TherapistId { get; set; }

        public int? ServiceId { get; set; }

        public DateTime? StartDateTime { get; set; }

        public DateTime? EndDateTime { get; set; }

        [MaxLength(30)]
        public string? Status { get; set; }

        [MaxLength(1000)]
        public string? PatientNotes { get; set; }
    }

    // PUT /api/admin/centers/{id}
    public class AdminUpdateCenterDto
    {
        [MaxLength(150)]
        public string? NameEnglish { get; set; }

        [MaxLength(150)]
        public string? NameArabic { get; set; }

        [MaxLength(100)]
        public string? City { get; set; }

        [MaxLength(300)]
        public string? Address { get; set; }

        [MaxLength(300)]
        public string? WebsiteUrl { get; set; }

        [MaxLength(20)]
        public string? PhoneNumber { get; set; }

        [MaxLength(1000)]
        public string? DescriptionEnglish { get; set; }
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

        //  تفضيلات إشعارات الأدمن: نفس الأعلام الأربعة العامة لكل الأدوار
        public AdminNotificationSettingsDto? Notifications { get; set; }
    }

    public class AdminNotificationSettingsDto
    {
        public bool? EmailEnabled { get; set; }

        public bool? InAppEnabled { get; set; }

        public bool? AppointmentReminders { get; set; }

        public bool? Marketing { get; set; }
    }
}
