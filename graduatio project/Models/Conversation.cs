namespace graduatio_project.Models
{
    public class Conversation
    {
        public int ConversationId { get; set; }

        public int PatientId { get; set; }

        public int CenterId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Patient Patient { get; set; } = null!;

        public Center Center { get; set; } = null!;
    }
}
