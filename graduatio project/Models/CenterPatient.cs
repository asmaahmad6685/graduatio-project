namespace graduatio_project.Models
{
    //  يربط المريض بالمركز الذي أضافه، حتى يظهر في قائمة مرضى ذلك المركز
    //  قبل أن يكون لديه أي موعد أو ملاحظة
    public class CenterPatient
    {
        public int CenterId { get; set; }

        public int PatientId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Center Center { get; set; } = null!;

        public Patient Patient { get; set; } = null!;
    }
}
