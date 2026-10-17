namespace graduatio_project.Services;

public sealed class SchedulingClock
{
    public const string TimeZoneId = "Asia/Hebron";
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _timeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    public SchedulingClock(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public DateTime Now => TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _timeZone).DateTime;

    public bool IsUnambiguousBlock(DateTime start, DateTime end, DateTime reservedUntil)
    {
        if (reservedUntil <= start)
            return false;

        var lastOccupiedTick = reservedUntil.AddTicks(-1);
        // A local-only appointment model cannot distinguish repeated DST times.
        // Check the occupied interval [start, reservedUntil), excluding its end boundary.
        foreach (var value in new[] { start, end, lastOccupiedTick })
        {
            if (_timeZone.IsInvalidTime(value) || _timeZone.IsAmbiguousTime(value))
                return false;
        }
        return _timeZone.GetUtcOffset(start) == _timeZone.GetUtcOffset(lastOccupiedTick);
    }
}
