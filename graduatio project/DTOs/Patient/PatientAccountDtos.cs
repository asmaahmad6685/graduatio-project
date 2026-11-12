using System.ComponentModel.DataAnnotations;

namespace graduatio_project.DTOs.Patient
{
    //  تعديل بيانات المريض الشخصية من صفحة الحساب
    public class UpdateProfileDto
    {
        [Required]
        [MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        [Phone]
        [MaxLength(20)]
        public string? PhoneNumber { get; set; }

        //  male أو female أو other بحالة غير حساسة
        [RegularExpression("^(?i:male|female|other)$",
            ErrorMessage = "Gender must be male, female or other.")]
        public string? Gender { get; set; }

        [DataType(DataType.Date)]
        public DateTime? DateOfBirth { get; set; }

        [MaxLength(100)]
        public string? City { get; set; }

        [MaxLength(500)]
        public string? ProfileImageUrl { get; set; }
    }

    //  تفضيلات الإشعارات لكل مستخدم
    public class UpdatePreferencesDto
    {
        public bool EmailEnabled { get; set; } = true;

        public bool InAppEnabled { get; set; } = true;

        public bool AppointmentReminders { get; set; } = true;

        public bool Marketing { get; set; }

        [RegularExpression("^(?i:en|ar)$",
            ErrorMessage = "Language must be en or ar.")]
        public string Language { get; set; } = "en";
    }
}