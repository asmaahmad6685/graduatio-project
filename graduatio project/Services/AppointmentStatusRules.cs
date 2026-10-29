using graduatio_project.Models;

namespace graduatio_project.Services
{
    //  تغيير حالة الموعد وتخزين السبب في الحقل الصحيح
    //  المركز والمعالج يستخدمان نفس القاعدة حتى لا يختلف الناتج
    public static class AppointmentStatusRules
    {
        public static void Apply(Appointment appointment, string status, string? reason)
        {
            var normalized = status.Trim().ToLowerInvariant();
            appointment.Status = char.ToUpperInvariant(normalized[0]) + normalized[1..];
            appointment.UpdatedAt = DateTime.UtcNow;

            if (appointment.Status == "Rejected")
            {
                appointment.RejectionReason = reason;
                appointment.CancellationReason = null;
            }
            else if (appointment.Status == "Cancelled")
            {
                appointment.CancellationReason = reason;
                appointment.RejectionReason = null;
            }
            else
            {
                appointment.RejectionReason = null;
                appointment.CancellationReason = null;
            }
        }
    }
}