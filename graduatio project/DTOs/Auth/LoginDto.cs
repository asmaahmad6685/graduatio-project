// ملاحظة: نستخدم DataAnnotations للتحقق من بيانات تسجيل الدخول
using System.ComponentModel.DataAnnotations;
namespace graduatio_project.DTOs.Auth
{
    public class LoginDto
    {
        // ملاحظة: هذا الـ DTO يستقبل بيانات تسجيل الدخول من الـ Frontend
      
            // ملاحظة: البريد الإلكتروني إجباري ويجب أن يكون بصيغة Email صحيحة
            [Required]
            [EmailAddress]
            [MaxLength(256)]
            public string Email { get; set; } = string.Empty;

            // ملاحظة: كلمة المرور إجبارية
            [Required]
            public string Password { get; set; } = string.Empty;
        }
}
