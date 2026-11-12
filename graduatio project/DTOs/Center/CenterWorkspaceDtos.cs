using System.ComponentModel.DataAnnotations;

namespace graduatio_project.DTOs.Center
{
    //  حفظ بيانات المركز من مساحته
    public class UpdateCenterInfoDto
    {
        [MaxLength(200)]
        public string? NameEnglish { get; set; }

        [MaxLength(200)]
        public string? NameArabic { get; set; }

        [MaxLength(1500)]
        public string? DescriptionEnglish { get; set; }

        [MaxLength(1500)]
        public string? DescriptionArabic { get; set; }

        [MaxLength(100)]
        public string? City { get; set; }

        [MaxLength(300)]
        public string? Address { get; set; }

        [MaxLength(300)]
        public string? AddressArabic { get; set; }

        [MaxLength(500)]
        public string? ProfileImageUrl { get; set; }

        [EmailAddress]
        [MaxLength(254)]
        public string? Email { get; set; }

        [MaxLength(30)]
        public string? PhoneNumber { get; set; }

        [MaxLength(500)]
        public string? WebsiteUrl { get; set; }

        public double? Latitude { get; set; }

        public double? Longitude { get; set; }

        public bool? LocationConfirmed { get; set; }
    }

    //  ساعات العمل: السبت إلى الخميس في weekStart/weekEnd وساعة الجمعة منفصلة
    public class UpdateWorkingHoursDto
    {
        [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$")]
        public string WeekStart { get; set; } = "08:00";

        [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$")]
        public string WeekEnd { get; set; } = "17:00";

        [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$")]
        public string FridayStart { get; set; } = "08:00";

        [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$")]
        public string FridayEnd { get; set; } = "13:00";
    }

    public class AddCenterImageDto
    {
        //  إما رفع ملف multipart بحقل file، أو تمرير رابط جاهز هنا
        //  لذلك الحقل اختياري: أحدهما على الأقل مطلوب
        [MaxLength(500)]
        public string? ImageUrl { get; set; }

        public bool IsPrimary { get; set; }
    }

    //  إضافة أو تعديل خدمة
    public class SaveServiceDto
    {
        [Required]
        [MaxLength(200)]
        public string NameEnglish { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string NameArabic { get; set; } = string.Empty;

        [MaxLength(1500)]
        public string? DescriptionEnglish { get; set; }

        [MaxLength(1500)]
        public string? DescriptionArabic { get; set; }

        [Range(0, 1000000)]
        public decimal Price { get; set; }

        [Range(5, 600)]
        public int DurationMinutes { get; set; }

        [Range(1, int.MaxValue)]
        public int SpecialtyId { get; set; }

        [MaxLength(500)]
        public string? ImageUrl { get; set; }

        public bool IsActive { get; set; } = true;
    }

    //  إضافة مريض من مساحة المركز: ينشئ حساب دخول وملف مريض ويربطه بالمركز
    public class CreateCenterPatientDto
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

        //  كلمة المرور التي يستخدمها المريض للدخول إلى حسابه
        [Required]
        [MinLength(8)]
        [MaxLength(100)]
        public string Password { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? Gender { get; set; }

        public DateOnly? DateOfBirth { get; set; }

        [MaxLength(500)]
        public string? ProfileImageUrl { get; set; }

        [MaxLength(100)]
        public string? City { get; set; }

        public bool IsActive { get; set; } = true;
    }

    //  تعديل بيانات مريض من مساحة المركز (بدون كلمة المرور)
    public class UpdateCenterPatientDto
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

        public DateOnly? DateOfBirth { get; set; }

        [MaxLength(500)]
        public string? ProfileImageUrl { get; set; }

        [MaxLength(100)]
        public string? City { get; set; }

        public bool IsActive { get; set; } = true;
    }

    //  تغيير حالة موعد من مساحة المركز
    public class UpdateAppointmentStatusDto
    {
        [Required]
        [RegularExpression("^(?i:approved|rejected|cancelled|completed|pending)$",
            ErrorMessage = "Status must be approved, rejected, cancelled, completed or pending.")]
        public string Status { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Reason { get; set; }
    }

    public class AddPatientNoteDto
    {
        [Required]
        [MaxLength(2000)]
        public string Text { get; set; } = string.Empty;
    }

    //  تعديل ملف معالج من مساحة المركز
    public class UpdateTherapistDto
    {
        [Required]
        [MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        [Range(1, int.MaxValue)]
        public int SpecialtyId { get; set; }

        [MaxLength(1000)]
        public string? Bio { get; set; }

        //  null = أبقِ ما هو محفوظ (نفس منطق Bio)
        [MaxLength(150)]
        public string? FullNameAr { get; set; }

        [MaxLength(1000)]
        public string? BioAr { get; set; }

        [MaxLength(500)]
        public string? ProfileImageUrl { get; set; }

        [Range(0, 60)]
        public int? YearsOfExperience { get; set; }

        [MaxLength(1000)]
        public string? Qualifications { get; set; }

        [MaxLength(300)]
        public string? Languages { get; set; }

        public bool IsAvailable { get; set; } = true;

        public bool IsActive { get; set; } = true;

        //  الخدمات التي يقدّمها المعالج؛ null = لا تغيير
        public List<int>? ServiceIds { get; set; }
    }

    public class ReplyToReviewDto
    {
        [Required]
        [MaxLength(1000)]
        public string Reply { get; set; } = string.Empty;
    }
}