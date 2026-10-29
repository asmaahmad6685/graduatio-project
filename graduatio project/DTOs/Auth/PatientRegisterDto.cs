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
        //  ونتحقق أنه رقم حقيقي بمقتضاها الدولي اختياري
        [Required]
        [MaxLength(20)]
        [RegularExpression(@"^\+?[0-9()\-\s]{7,20}$",
            ErrorMessage = "PhoneNumber must be a valid phone number.")]
        public string PhoneNumber { get; set; } = string.Empty;

        //  كلمة المرور إجبارية ويجب ألا تقل عن 8 أحرف
        [Required]
        [MinLength(8)]
        public string Password { get; set; } = string.Empty;

      

        // جنس المريض اختياري، وإذا تم إدخاله فيجب أن يكون male أو female
        // نقبل الحالة الكبيرة والصغيرة لأن الواجهة قد ترسل أيًّا منهما
        [MaxLength(20)]
        [RegularExpression(@"^(?i:male|female)$",
            ErrorMessage = "Gender must be either male or female.")]
        public string? Gender { get; set; }


    }
}
