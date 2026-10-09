using System.ComponentModel.DataAnnotations;
namespace graduatio_project.DTOs.Auth
{
    public class CenterRegisterDto
    {
        //  اسم المركز إجباري وأقصى طول له 200 حرف
        [Required]
        [MaxLength(200)]
        public string CenterName { get; set; } = string.Empty;

        //  البريد الإلكتروني للمركز إجباري ويجب أن يكون بصيغة صحيحة
        [Required]
        [EmailAddress]
        [MaxLength(256)]
        public string Email { get; set; } = string.Empty;

        //  رقم هاتف المركز إجباري وأقصى طول له 20 حرف
        [Required]
        [MaxLength(20)]
        public string PhoneNumber { get; set; } = string.Empty;

        //  هذا هو الموقع الذي يدخله المركز في حقل Center location في واجهة التسجيل
        [Required]
        [MaxLength(300)]
        public string Location { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string City { get; set; } = string.Empty;

        //  إحداثيات المركز اختيارية وتُرسل عند استخدام "موقعي الحالي"
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        //  كلمة مرور حساب المركز ويجب ألا تقل عن 8 أحرف
        [Required]
        [MinLength(8)]
        public string Password { get; set; } = string.Empty;
    }
}
