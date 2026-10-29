namespace graduatio_project.Models
{
    //  تفضيلات الإشعارات لكل مستخدم
    //  نستخدمها في حساب المريض والمركز والمعالج لنفس الشكل
    public class NotificationPreference
    {
        public int NotificationPreferenceId { get; set; }

        public int UserId { get; set; }

        //  إشعارات البريد الإلكتروني
        public bool EmailEnabled { get; set; } = true;

        //  الإشعارات داخل التطبيق
        public bool InAppEnabled { get; set; } = true;

        //  تذكيرات المواعيد
        public bool AppointmentReminders { get; set; } = true;

        //  رسائل تسويقية
        public bool Marketing { get; set; } = false;

        //  لغة الواجهة المفضلة: "en" أو "ar"
        public string Language { get; set; } = "en";

        public User User { get; set; } = null!;
    }
}
