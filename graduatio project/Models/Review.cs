namespace graduatio_project.Models
{
    public class Review
    {
        public int CenterId { get; set; }

        public int AppointmentId { get; set; }

        public int Rating { get; set; }

        public string? Comment { get; set; }

        public bool IsVisible { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public Patient Patient { get; set; } = null!;

        public Center Center { get; set; } = null!;

        public Appointment Appointment { get; set; } = null!;
    }
}
