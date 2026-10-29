using System.ComponentModel.DataAnnotations;

namespace graduatio_project.DTOs.Admin
{
    //  قرار الأدمن على طلب افتتاح مركز
    public class ReviewCenterApplicationDto
    {
        [Required]
        [RegularExpression("^(?i:approved|rejected)$",
            ErrorMessage = "Decision must be approved or rejected.")]
        public string Decision { get; set; } = string.Empty;

        //  السبب إجباري عند الرفض حتى يظهر لصاحب الطلب
        [MaxLength(500)]
        public string? Reason { get; set; }
    }
}