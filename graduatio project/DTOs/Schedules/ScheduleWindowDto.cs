namespace graduatio_project.DTOs.Schedules;

public sealed record ScheduleWindowDto(
    int ScheduleId,
    int TherapistId,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool IsAvailable);
