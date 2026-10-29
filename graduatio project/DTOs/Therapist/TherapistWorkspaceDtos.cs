using System.ComponentModel.DataAnnotations;

namespace graduatio_project.DTOs.Therapist
{
    //  تغيير حالة الموعد من مساحة المعالج
    public class TherapistAppointmentStatusDto
    {
        [Required]
        [RegularExpression("^(?i:approved|rejected|cancelled|completed|pending)$",
            ErrorMessage = "Status must be approved, rejected, cancelled, completed or pending.")]
        public string Status { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Reason { get; set; }
    }

    //  إضافة مواعيد المعالج: السبت إلى الخميس
    public class SaveScheduleDto
    {
        [Required]
        [RegularExpression("^(?i:sunday|monday|tuesday|wednesday|thursday|friday|saturday)$")]
        public string DayOfWeek { get; set; } = string.Empty;

        [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$")]
        public string StartTime { get; set; } = "09:00";

        [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$")]
        public string EndTime { get; set; } = "16:00";

        public bool IsAvailable { get; set; } = true;
    }
}