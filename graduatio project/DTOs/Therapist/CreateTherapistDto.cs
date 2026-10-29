using System.ComponentModel.DataAnnotations;

namespace graduatio_project.DTOs.Therapist
{
    public class CreateTherapistDto
    {
        //  الاسم الكامل للمعالج
        [Required]
        [MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        //  الإيميل الذي سيستخدمه المعالج في Login
        // ويجب أن يكون بصيغة Email صحيحة
        [Required]
        [EmailAddress]
        [MaxLength(256)]
        public string Email { get; set; } = string.Empty;

        //  كلمة المرور التي سيستخدمها المعالج لتسجيل الدخول
        [Required]
        [MinLength(8)]
        public string Password { get; set; } = string.Empty;

        //  التخصص الخاص بالمعالج
        [Required]
        public int SpecialtyId { get; set; }

        //  نبذة تعريفية عن المعالج - اختيارية
        [MaxLength(1000)]
        public string? Bio { get; set; }

        //  عدد سنوات الخبرة - اختياري
        [Range(0, int.MaxValue)]
        public int? YearsOfExperience { get; set; }

        //  المؤهلات والشهادات - اختيارية
        [MaxLength(1000)]
        public string? Qualifications { get; set; }

        //  اللغات التي يتحدثها المعالج - اختيارية
        [MaxLength(300)]
        public string? Languages { get; set; }

        //  رابط أو مسار صورة المعالج - اختياري
        [MaxLength(500)]
        public string? ProfileImageUrl { get; set; }
    }
}
