namespace graduatio_project.Models
{
    public class PatientSavedCenter
    {
        public int PatientId { get; set; }

        public int CenterId { get; set; }

        public DateTime SavedAt { get; set; } = DateTime.UtcNow;

        public Patient Patient { get; set; } = null!;

        public Center Center { get; set; } = null!;
    }
}
