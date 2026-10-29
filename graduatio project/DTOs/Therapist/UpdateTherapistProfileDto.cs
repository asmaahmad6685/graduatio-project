using System.ComponentModel.DataAnnotations;

namespace graduatio_project.DTOs.Therapist
{
    public class UpdateTherapistProfileDto
    {
        //  الاسم الكامل للمعالج
        [Required]
        [MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        //  التخصص الخاص بالمعالج
        [Required]
        public int SpecialtyId { get; set; }

        //  نبذة تعريفية عن المعالج - اختيارية
        [MaxLength(1000)]
        public string? Bio { get; set; }
// رابط أو مسار صورة المعالج - اختياري
        [MaxLength(500)]
        public string? ProfileImageUrl { get; set; }

        //  عدد سنوات الخبرة - اختياري
        [Range(0, int.MaxValue)]
        public int? YearsOfExperience { get; set; }

        //  المؤهلات والشهادات - اختيارية
        [MaxLength(1000)]
        public string? Qualifications { get; set; }

        //  اللغات التي يتحدثها المعالج - اختيارية
        [MaxLength(300)]
        public string? Languages { get; set; }

        // يحدد إذا كان المعالج متاحاً حالياً أم لا
        public bool IsAvailable { get; set; }
    }
}

