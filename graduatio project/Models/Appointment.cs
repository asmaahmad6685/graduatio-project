namespace graduatio_project.Models
{
    public class Appointment
    {

        public int AppointmentId { get; set; }

        public int PatientId { get; set; }

        public int CenterId { get; set; }

        public int TherapistId { get; set; }

        public int ServiceId { get; set; }

        public DateTime StartDateTime { get; set; }

        public DateTime EndDateTime { get; set; }

        public decimal PriceAtBooking { get; set; }

        public string? PatientNotes { get; set; }

        public string Status { get; set; } = "Pending";

        public string? RejectionReason { get; set; }

        public string? CancellationReason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public Patient Patient { get; set; } = null!;

        public Center Center { get; set; } = null!;

        public Therapist Therapist { get; set; } = null!;

        public Service Service { get; set; } = null!;
    }
}
