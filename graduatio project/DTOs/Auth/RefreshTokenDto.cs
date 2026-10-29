using System.ComponentModel.DataAnnotations;

namespace graduatio_project.DTOs.Auth
{
    //  يحمل الـ Refresh Token الذي أرسله الـ Frontend عند التجديد
    public class RefreshTokenDto
    {
        [Required]
        public string RefreshToken { get; set; } = string.Empty;
    }

    //  عند تسجيل الخروج نُلغي الـ Refresh Token الحالي إن أُرسل
    //  لذلك الحقل اختياري حتى لا يفشل الـ API إذا لم يحفظه الواجهة
    public class LogoutDto
    {
        public string? RefreshToken { get; set; }
    }
}
