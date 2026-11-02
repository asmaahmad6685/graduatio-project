using System.ComponentModel.DataAnnotations;

namespace graduatio_project.DTOs.Messaging
{
    //  إرسال رسالة داخل محادثة قائمة
    //  النص لم يعد مطلوبًا منفصلًا: رسالة مرفق فقط مسموح بها،
    //  لكن يجب أن تحمل الرسالة نصًا أو مرفقًا على الأقل (يتحقق الـ Controller)
    public class SendMessageDto
    {
        [MaxLength(2000)]
        public string? Text { get; set; }

        //  رابط مرفق اختياري: يرفعه المريض عبر POST /api/patient/account/image
        //  أو المعالج عبر POST /api/therapist/workspace/images ثم يُرسل الرابط هنا
        [MaxLength(500)]
        public string? Attachment { get; set; }
    }

    //  فتح محادثة بين المريض الحالي ومعالج (إنشاء أو إيجاد الموجود)
    public sealed class OpenConversationDto
    {
        [Range(1, int.MaxValue)]
        public int TherapistId { get; set; }
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