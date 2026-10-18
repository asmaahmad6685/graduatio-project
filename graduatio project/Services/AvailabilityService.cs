using graduatio_project.Data;
using graduatio_project.DTOs.Availability;
using Microsoft.EntityFrameworkCore;

namespace graduatio_project.Services;

public sealed class AvailabilityService
{
    private readonly ApplicationDbContext _context;
    private readonly SchedulingClock _clock;

    public AvailabilityService(ApplicationDbContext context, SchedulingClock clock)
    {
        _context = context;
        _clock = clock;
    }

    public Task<AvailabilityResponseDto> GetAsync(int therapistId, int serviceId, DateOnly date,
        CancellationToken cancellationToken = default) =>
        CalculateAsync(therapistId, serviceId, date, null, cancellationToken);

    internal Task<AvailabilityResponseDto> GetForBookingAsync(int therapistId, int serviceId, DateOnly date,
        int? excludedAppointmentId, CancellationToken cancellationToken = default) =>
        CalculateAsync(therapistId, serviceId, date, excludedAppointmentId, cancellationToken);

    private async Task<AvailabilityResponseDto> CalculateAsync(int therapistId, int serviceId, DateOnly date,
        int? excludedAppointmentId, CancellationToken cancellationToken)
    {
        var now = _clock.Now;
        var today = DateOnly.FromDateTime(now);
        var bookableThrough = today.AddMonths(1);
        if (date < today || date > bookableThrough)
            throw new AvailabilityValidationException("Date must be from today through one calendar month ahead, inclusive.");
        if (serviceId <= 0)
            throw new AvailabilityValidationException("ServiceId must be a positive integer.");

        var therapist = await _context.Therapists.AsNoTracking().Include(value => value.Center)
            .SingleOrDefaultAsync(value => value.TherapistId == therapistId, cancellationToken);
        if (therapist == null)
            throw new AvailabilityNotFoundException("Therapist not found.");
        var service = await _context.Services.AsNoTracking()
            .SingleOrDefaultAsync(value => value.ServiceId == serviceId, cancellationToken);
        if (service == null)
            throw new AvailabilityNotFoundException("Service not found.");

        AvailabilityResponseDto Response(IReadOnlyList<AvailabilitySlotDto> slots) =>
            new(therapistId, serviceId, date, SchedulingClock.TimeZoneId,
                SchedulingRules.TreatmentMinutes, SchedulingRules.BreakMinutes, bookableThrough, slots);

        if (!therapist.IsActive || !therapist.IsAvailable
            || !SchedulingRules.IsBookableCenter(therapist.Center.Status)
            || !service.IsActive || service.CenterId != therapist.CenterId
            || !await _context.TherapistServices.AsNoTracking().AnyAsync(value =>
                value.TherapistId == therapistId && value.ServiceId == serviceId, cancellationToken))
            return Response([]);

        var hours = await _context.CenterWorkingHours.AsNoTracking().SingleOrDefaultAsync(value =>
            value.CenterId == therapist.CenterId && value.DayOfWeek == date.DayOfWeek, cancellationToken);
        if (hours == null || hours.IsClosed || !hours.OpeningTime.HasValue || !hours.ClosingTime.HasValue
            || hours.OpeningTime.Value >= hours.ClosingTime.Value)
            return Response([]);

        var windows = await _context.TherapistSchedules.AsNoTracking().Where(value =>
            value.TherapistId == therapistId && value.DayOfWeek == date.DayOfWeek && value.IsAvailable)
            .ToListAsync(cancellationToken);
        var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var dayEnd = dayStart.AddDays(1);
        var opening = date.ToDateTime(hours.OpeningTime.Value, DateTimeKind.Unspecified);
        var closing = date.ToDateTime(hours.ClosingTime.Value, DateTimeKind.Unspecified);
        var earliestEnd = dayStart.AddMinutes(-SchedulingRules.BreakMinutes);
        var earliestStart = dayStart.AddMinutes(-SchedulingRules.SlotMinutes);

        // Read across all services and include appointments spilling over from a previous day.
        // UPPER is translated by both SQL Server and SQLite for case-insensitive status matching.
        var appointments = await _context.Appointments.AsNoTracking().Where(value =>
            value.TherapistId == therapistId
            && (!excludedAppointmentId.HasValue || value.AppointmentId != excludedAppointmentId.Value)
            && (value.Status.ToUpper() == "PENDING" || value.Status.ToUpper() == "CONFIRMED")
            && value.StartDateTime < dayEnd
            && (value.EndDateTime > earliestEnd || value.StartDateTime > earliestStart))
            .Select(value => new { value.StartDateTime, value.EndDateTime })
            .ToListAsync(cancellationToken);
        var occupied = appointments.Select(value => (
            Start: DateTime.SpecifyKind(value.StartDateTime, DateTimeKind.Unspecified),
            End: SchedulingRules.GetOccupiedEnd(value.StartDateTime, value.EndDateTime))).ToList();

        var candidates = new List<AvailabilitySlotDto>();
        foreach (var window in windows.Where(value => value.StartTime < value.EndTime))
        {
            var windowEnd = date.ToDateTime(window.EndTime, DateTimeKind.Unspecified);
            for (var start = date.ToDateTime(window.StartTime, DateTimeKind.Unspecified);
                start.AddMinutes(SchedulingRules.SlotMinutes) <= windowEnd;
                start = start.AddMinutes(SchedulingRules.SlotMinutes))
            {
                var end = start.AddMinutes(SchedulingRules.TreatmentMinutes);
                var reservedUntil = start.AddMinutes(SchedulingRules.SlotMinutes);
                if (start < now || start < opening || reservedUntil > closing
                    || !_clock.IsUnambiguousBlock(start, end, reservedUntil)
                    || occupied.Any(value => start < value.End && value.Start < reservedUntil))
                    continue;
                candidates.Add(new(start, end, reservedUntil));
            }
        }

        // Normal schedule writes prevent overlaps. Also handle overlapping/duplicate legacy
        // windows conservatively so a response never advertises competing occupied blocks.
        var result = new List<AvailabilitySlotDto>();
        foreach (var candidate in candidates.DistinctBy(value => value.StartDateTime)
            .OrderBy(value => value.StartDateTime))
        {
            if (result.Count == 0 || candidate.StartDateTime >= result[^1].ReservedUntil)
                result.Add(candidate);
        }
        return Response(result);
    }
}

public sealed class AvailabilityValidationException(string message) : Exception(message);
public sealed class AvailabilityNotFoundException(string message) : Exception(message);
