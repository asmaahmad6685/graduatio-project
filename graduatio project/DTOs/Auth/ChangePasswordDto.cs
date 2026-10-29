using System.ComponentModel.DataAnnotations;

namespace graduatio_project.DTOs.Auth
{
    //  يُستخدم لتغيير كلمة المرور من صفحة إعدادات المستخدم
    public class ChangePasswordDto
    {
        [Required]
        public string CurrentPassword { get; set; } = string.Empty;

        //  نفس قاعدة التسجيل: 8 أحرف على الأقل
        [Required]
        [MinLength(8)]
        public string NewPassword { get; set; } = string.Empty;
    }
}
