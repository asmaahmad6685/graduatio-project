namespace graduatio_project.Models
{
    public class Patient
    {
        public int PatientId { get; set; }

        public int UserId { get; set; }

        public string FullName { get; set; } = string.Empty;

        public string? Gender { get; set; }

        public string? ProfileImageUrl { get; set; }

        public User User { get; set; } = null!;
    }
}
