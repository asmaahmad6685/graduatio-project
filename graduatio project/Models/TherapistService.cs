namespace graduatio_project.Models
{
    public class TherapistService
    {
        public int TherapistId { get; set; }

        public int ServiceId { get; set; }

        public Therapist Therapist { get; set; } = null!;

        public Service Service { get; set; } = null!;
    }
}
