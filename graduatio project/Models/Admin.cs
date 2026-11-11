namespace graduatio_project.Models
{
    public class Admin
    {
        public int AdminId { get; set; }

        public int UserId { get; set; }

        public string FullName { get; set; } = string.Empty;

        //  صورة الأدمن الشخصية (ملف مرفوع أو رابط خارجي)
        public string? ProfileImageUrl { get; set; }

        public User User { get; set; } = null!;
    }
}
