namespace graduatio_project.Models
{
    public class AppointmentStatusHistory
    {
        public int AppointmentStatusHistoryId { get; set; }

        public int AppointmentId { get; set; }

        public string? OldStatus { get; set; }

        public string NewStatus { get; set; } = string.Empty;

        public int? ChangedByUserId { get; set; }

        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

        public Appointment Appointment { get; set; } = null!;

        public User? ChangedByUser { get; set; }
    }
}
