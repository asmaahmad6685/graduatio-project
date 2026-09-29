namespace graduatio_project.Models
{
    public class CenterDocument
    {
        public int CenterDocumentId { get; set; }

        public int CenterId { get; set; }

        public string DocumentType { get; set; } = string.Empty;

        public string FileUrl { get; set; } = string.Empty;

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        public Center Center { get; set; } = null!;
    }
}
