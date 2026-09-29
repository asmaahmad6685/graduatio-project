namespace graduatio_project.Models
{
    public class Message
    {
        public int MessageId { get; set; }

        public int ConversationId { get; set; }

        public int SenderUserId { get; set; }

        public string MessageText { get; set; } = string.Empty;

        public bool IsRead { get; set; } = false;

        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        public Conversation Conversation { get; set; } = null!;

        public User SenderUser { get; set; } = null!;
    }
}
