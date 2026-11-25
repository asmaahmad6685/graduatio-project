namespace graduatio_project.Models
{
    public class Center
    {
        public int CenterId { get; set; }

        public int UserId { get; set; }

        public string NameArabic { get; set; } = string.Empty;

        public string? NameEnglish { get; set; }

        public string? DescriptionArabic { get; set; }

        public string? DescriptionEnglish { get; set; }

        public string City { get; set; } = string.Empty;

        public string Address { get; set; } = string.Empty;

        public string? AddressArabic { get; set; }

        public string? ProfileImageUrl { get; set; }

        public string? WebsiteUrl { get; set; }

        public string Status { get; set; } = "Pending";

        public string? RejectionReason { get; set; }

        public int? ApprovedByAdminId { get; set; }

        public DateTime? ApprovedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public User User { get; set; } = null!;

        public Admin? ApprovedByAdmin { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        //  هل أكد المركز إحداثياته على الخريطة
        public bool LocationConfirmed { get; set; } = false;

        //  وقت مراجعة طلب التسجيل من قبل الأدمن (موافقة أو رفض)
        public DateTime? ReviewedAt { get; set; }
    }
}
