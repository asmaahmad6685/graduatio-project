namespace graduatio_project.Models
{
    public class TherapistSchedule
    {
        public int ScheduleId { get; set; }

        public int TherapistId { get; set; }

        public DayOfWeek DayOfWeek { get; set; }

        public TimeOnly StartTime { get; set; }

        public TimeOnly EndTime { get; set; }

        public bool IsAvailable { get; set; } = true;

        public Therapist Therapist { get; set; } = null!;
    }
}
