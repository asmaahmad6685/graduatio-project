using System.ComponentModel.DataAnnotations;

namespace graduatio_project.DTOs.Auth
{
    public class ResetPasswordDto
    {
        // هذا هو الـ Token الخاص بإعادة تعيين كلمة المرور
        // الـ Backend سيستخدمه للتأكد أن طلب Reset Password صالح
        [Required]
        public string Token { get; set; } = string.Empty;

        //  كلمة المرور الجديدة التي يريد المستخدم استخدامها
        [Required]
        [MinLength(8)]
        public string NewPassword { get; set; } = string.Empty;

        //  المستخدم يكتب كلمة المرور الجديدة مرة ثانية للتأكيد
        // Compare تتأكد أنها مطابقة لـ NewPassword
        [Required]
        [Compare(nameof(NewPassword))]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
