namespace graduatio_project.DTOs.Availability;

public sealed record AvailabilityResponseDto(
    int TherapistId,
    int ServiceId,
    DateOnly Date,
    string TimeZone,
    int SessionMinutes,
    int BreakMinutes,
    DateOnly BookableThrough,
    IReadOnlyList<AvailabilitySlotDto> Slots);

// These are local Asia/Hebron wall-clock values, intentionally without a UTC suffix.
public sealed record AvailabilitySlotDto(
    DateTime StartDateTime,
    DateTime EndDateTime,
    DateTime ReservedUntil);
