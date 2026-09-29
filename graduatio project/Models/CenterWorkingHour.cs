namespace graduatio_project.Models
{
    public class CenterWorkingHour
    {
        public int WorkingHourId { get; set; }

        public int CenterId { get; set; }

        public DayOfWeek DayOfWeek { get; set; }

        public TimeOnly? OpeningTime { get; set; }

        public TimeOnly? ClosingTime { get; set; }

        public bool IsClosed { get; set; } = false;

        public Center Center { get; set; } = null!;
    }
}
