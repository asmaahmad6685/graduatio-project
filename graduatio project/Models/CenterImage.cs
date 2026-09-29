namespace graduatio_project.Models
{
    public class CenterImage
    {
        public int CenterImageId { get; set; }

        public int CenterId { get; set; }

        public string ImageUrl { get; set; } = string.Empty;

        public bool IsPrimary { get; set; } = false;

        public int DisplayOrder { get; set; }

        public Center Center { get; set; } = null!;
    }
}
