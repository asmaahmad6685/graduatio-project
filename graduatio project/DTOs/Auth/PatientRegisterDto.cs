// ملاحظة: نستخدم DataAnnotations لوضع قواعد التحقق على بيانات التسجيل
using System.ComponentModel.DataAnnotations;
namespace graduatio_project.DTOs.Auth
{
    public class PatientRegisterDto
    {
        //  الاسم الكامل إجباري وأقصى طول له 150 حرف
        [Required]
        [MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        //  البريد الإلكتروني إجباري ويجب أن يكون بصيغة Email صحيحة
        [Required]
        [EmailAddress]
        [MaxLength(256)]
        public string Email { get; set; } = string.Empty;

        //  رقم الهاتف إجباري وأقصى طول له 20 حرف
        [Required]
        [MaxLength(20)]
        public string PhoneNumber { get; set; } = string.Empty;

        //  كلمة المرور إجبارية ويجب ألا تقل عن 8 أحرف
        [Required]
        [MinLength(8)]
        public string Password { get; set; } = string.Empty;

      

        // جنس المريض اختياري، وإذا تم إدخاله فأقصى طول له 20 حرف
        [MaxLength(20)]
        public string? Gender { get; set; }


    }
}
