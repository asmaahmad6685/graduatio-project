using System.ComponentModel.DataAnnotations;

namespace graduatio_project.DTOs.Appointments
{
    //  طلب حجز موعد جديد
    //  المعرفات تصل كأرقام أو نصوص — نقبل الاثنين في FlexibleIntConverter
    public class AppointmentRequestDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "CenterId is required.")]
        public int CenterId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "TherapistId is required.")]
        public int TherapistId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "ServiceId is required.")]
        public int ServiceId { get; set; }

        //  التاريخ بصيغة YYYY-MM-DD
        [Required]
        [RegularExpression(@"^\d{4}-\d{2}-\d{2}$",
            ErrorMessage = "Date must be in YYYY-MM-DD format.")]
        public string Date { get; set; } = string.Empty;

        //  الوقت بصيغة HH:MM بنظام 24 ساعة
        [Required]
        [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$",
            ErrorMessage = "Time must be in HH:MM 24-hour format.")]
        public string Time { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Notes { get; set; }
    }
}