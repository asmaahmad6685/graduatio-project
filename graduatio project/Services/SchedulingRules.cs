namespace graduatio_project.Services;

public static class SchedulingRules
{
    public const int TreatmentMinutes = 50;
    public const int BreakMinutes = 10;
    public const int SlotMinutes = TreatmentMinutes + BreakMinutes;

    public static bool IsBlockingStatus(string? status) =>
        string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Confirmed", StringComparison.OrdinalIgnoreCase);

    public static bool IsBookableCenter(string? status) =>
        string.Equals(status, "Active", StringComparison.Ordinal);

    public static DateTime GetOccupiedEnd(DateTime start, DateTime end)
    {
        start = DateTime.SpecifyKind(start, DateTimeKind.Unspecified);
        end = DateTime.SpecifyKind(end, DateTimeKind.Unspecified);
        try
        {
            var minimumTreatmentEnd = start.AddMinutes(TreatmentMinutes);
            return (end > minimumTreatmentEnd ? end : minimumTreatmentEnd).AddMinutes(BreakMinutes);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Malformed legacy timestamps must not accidentally expose a free slot.
            return DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Unspecified);
        }
    }
}
