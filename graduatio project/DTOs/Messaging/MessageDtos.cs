using System.ComponentModel.DataAnnotations;

namespace graduatio_project.DTOs.Messaging
{
    //  إرسال رسالة داخل محادثة قائمة
    public class SendMessageDto
    {
        [Required]
        [MaxLength(2000)]
        public string Text { get; set; } = string.Empty;

        //  رابط مرفق اختياري: يرفعه المريض عبر POST /api/patient/account/image
        //  ثم يرسل الرابط الناتج هنا
        [MaxLength(500)]
        public string? Attachment { get; set; }
    }

    //  تعليم رسائل كمقروءة
    public class MarkMessagesReadDto
    {
        [MinLength(1)]
        public List<int> MessageIds { get; set; } = new();
    }

    //  تعليم إشعار كمقروء
    public class MarkNotificationReadDto
    {
        [Range(1, int.MaxValue)]
        public int Id { get; set; }
    }
}