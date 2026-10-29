namespace graduatio_project.Models
{
    //  يمثل Refresh Token واحد صادر لمستخدم بعد تسجيل الدخول أو بعد تجديد التوكن
    //  لا نخزن التوكن الأصلي، فقط الـ Hash منه حتى لو سُرقت قاعدة البيانات لا ينفع التوكن
    public class RefreshToken
    {
        public int RefreshTokenId { get; set; }

        public int UserId { get; set; }

        //  SHA-256 hex (64 hex chars) للتوكن الأصلي
        public string TokenHash { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; }

        //  إذا كان مُستخدَمًا أو مُلغى نملأ هذا الحقل
        public DateTime? RevokedAt { get; set; }

        //  عند التجديد نربط التوكن القديم بالتوكن الجديد حتى نكتشف إعادة الاستخدام
        public string? ReplacedByTokenHash { get; set; }

        //  نص يحدد الجهاز/المتصفح، اختياري ومساعد للتدقيق فقط
        public string? DeviceInfo { get; set; }

        public User User { get; set; } = null!;

        //  التوكن صالح فقط إذا لم يُلغَ ولم ينتهِ صلاحيته
        public bool IsActive => RevokedAt == null && ExpiresAt > DateTime.UtcNow;
    }
}
