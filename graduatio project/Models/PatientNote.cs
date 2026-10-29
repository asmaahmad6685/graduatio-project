namespace graduatio_project.Models
{
    //  ملاحظة يكتبها فريق المركز على مريض داخل مساحته
    //  تظهر لفريق المركز نفسه فقط ولا تظهر للمريض
    public class PatientNote
    {
        public int PatientNoteId { get; set; }

        public int CenterId { get; set; }

        public int PatientId { get; set; }

        //  من كتب الملاحظة: مستخدم المركز الذي أرسل الطلب
        public int AuthorUserId { get; set; }

        public string Text { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Center Center { get; set; } = null!;

        public Patient Patient { get; set; } = null!;
    }
}