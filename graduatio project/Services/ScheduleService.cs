using System.ComponentModel.DataAnnotations;
using System.Data;
using graduatio_project.Data;
using graduatio_project.DTOs.Schedules;
using graduatio_project.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace graduatio_project.Services;

public sealed class ScheduleService
{
    private static readonly SemaphoreSlim WriteLock = new(1, 1);
    private readonly ApplicationDbContext _context;

    public ScheduleService(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<int?> ResolveCenterIdAsync(int userId, CancellationToken cancellationToken = default) =>
        _context.Centers.AsNoTracking().Where(center => center.UserId == userId)
            .Select(center => (int?)center.CenterId).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ScheduleWindowDto>?> GetAsync(int centerId, int therapistId,
        CancellationToken cancellationToken = default)
    {
        if (!await OwnsTherapistAsync(centerId, therapistId, cancellationToken))
            return null;

        return await _context.TherapistSchedules.AsNoTracking()
            .Where(window => window.TherapistId == therapistId && window.Therapist.CenterId == centerId)
            .OrderBy(window => window.DayOfWeek).ThenBy(window => window.StartTime)
            .ThenBy(window => window.ScheduleId)
            .Select(window => new ScheduleWindowDto(window.ScheduleId, window.TherapistId,
                window.DayOfWeek, window.StartTime, window.EndTime, window.IsAvailable))
            .ToListAsync(cancellationToken);
    }

    public Task<ScheduleWindowDto?> CreateAsync(int centerId, int therapistId, ScheduleWindowRequestDto request,
        CancellationToken cancellationToken = default) =>
        WriteAsync<ScheduleWindowDto?>(therapistId, async () =>
        {
            if (!await OwnsTherapistAsync(centerId, therapistId, cancellationToken))
                return null;

            ValidateRequest(request);
            var candidate = CreateWindow(therapistId, request);
            await ValidateWorkingHoursAsync(centerId, candidate, cancellationToken);
            var windows = await _context.TherapistSchedules.AsNoTracking()
                .Where(window => window.TherapistId == therapistId).ToListAsync(cancellationToken);
            ValidateNoOverlap(candidate, windows);

            _context.TherapistSchedules.Add(candidate);
            await _context.SaveChangesAsync(cancellationToken);
            return ToDto(candidate);
        }, cancellationToken);

    public Task<ScheduleWindowDto?> UpdateAsync(int centerId, int therapistId, int scheduleId,
        ScheduleWindowRequestDto request, CancellationToken cancellationToken = default) =>
        WriteAsync<ScheduleWindowDto?>(therapistId, async () =>
        {
            if (!await OwnsTherapistAsync(centerId, therapistId, cancellationToken))
                return null;

            var existing = await _context.TherapistSchedules.SingleOrDefaultAsync(window =>
                window.ScheduleId == scheduleId && window.TherapistId == therapistId
                && window.Therapist.CenterId == centerId, cancellationToken);
            if (existing == null)
                return null;

            ValidateRequest(request);
            var candidate = CreateWindow(therapistId, request);
            candidate.ScheduleId = scheduleId;
            await ValidateWorkingHoursAsync(centerId, candidate, cancellationToken);
            var otherWindows = await _context.TherapistSchedules.AsNoTracking()
                .Where(window => window.TherapistId == therapistId && window.ScheduleId != scheduleId)
                .ToListAsync(cancellationToken);
            ValidateNoOverlap(candidate, otherWindows);
            var resultingWindows = otherWindows.Append(candidate).ToList();
            await ProtectAppointmentsAsync(therapistId, [existing.DayOfWeek, candidate.DayOfWeek],
                resultingWindows, cancellationToken);

            existing.DayOfWeek = candidate.DayOfWeek;
            existing.StartTime = candidate.StartTime;
            existing.EndTime = candidate.EndTime;
            existing.IsAvailable = candidate.IsAvailable;
            await _context.SaveChangesAsync(cancellationToken);
            return ToDto(existing);
        }, cancellationToken);

    public Task<bool> DeleteAsync(int centerId, int therapistId, int scheduleId,
        CancellationToken cancellationToken = default) =>
        WriteAsync(therapistId, async () =>
        {
            if (!await OwnsTherapistAsync(centerId, therapistId, cancellationToken))
                return false;

            var existing = await _context.TherapistSchedules.SingleOrDefaultAsync(window =>
                window.ScheduleId == scheduleId && window.TherapistId == therapistId
                && window.Therapist.CenterId == centerId, cancellationToken);
            if (existing == null)
                return false;

            var resultingWindows = await _context.TherapistSchedules.AsNoTracking()
                .Where(window => window.TherapistId == therapistId && window.ScheduleId != scheduleId)
                .ToListAsync(cancellationToken);
            await ProtectAppointmentsAsync(therapistId, [existing.DayOfWeek], resultingWindows, cancellationToken);
            _context.TherapistSchedules.Remove(existing);
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);

    private Task<bool> OwnsTherapistAsync(int centerId, int therapistId, CancellationToken cancellationToken) =>
        _context.Therapists.AsNoTracking().AnyAsync(therapist => therapist.TherapistId == therapistId
            && therapist.CenterId == centerId, cancellationToken);

    private async Task ValidateWorkingHoursAsync(int centerId, TherapistSchedule candidate,
        CancellationToken cancellationToken)
    {
        var hours = await _context.CenterWorkingHours.AsNoTracking()
            .Where(hour => hour.CenterId == centerId && hour.DayOfWeek == candidate.DayOfWeek)
            .ToListAsync(cancellationToken);
        if (hours.Count != 1 || hours[0].IsClosed || hours[0].OpeningTime == null
            || hours[0].ClosingTime == null || hours[0].OpeningTime >= hours[0].ClosingTime)
        {
            throw new SchedulingValidationException(new Dictionary<string, string[]>
            {
                [nameof(ScheduleWindowRequestDto.DayOfWeek)] =
                    ["The Center must have valid, open working hours for this day."]
            });
        }

        if (candidate.StartTime < hours[0].OpeningTime!.Value || candidate.EndTime > hours[0].ClosingTime!.Value)
        {
            throw new SchedulingValidationException(new Dictionary<string, string[]>
            {
                [nameof(ScheduleWindowRequestDto.StartTime)] =
                    ["The entire schedule window must be inside the Center's working hours."]
            });
        }
    }

    private static void ValidateNoOverlap(TherapistSchedule candidate, IReadOnlyList<TherapistSchedule> windows)
    {
        var sameDayWindows = windows.Where(window => window.DayOfWeek == candidate.DayOfWeek).ToList();
        if (sameDayWindows.Any(window => window.StartTime >= window.EndTime))
            throw new SchedulingConflictException("An existing schedule window on this day has invalid time bounds.");

        // Unavailable windows also reserve their interval in the weekly schedule; adjacent intervals are valid.
        if (sameDayWindows.Any(window => candidate.StartTime < window.EndTime && candidate.EndTime > window.StartTime))
            throw new SchedulingConflictException("Schedule windows for the same therapist and day must not overlap.");
    }

    private async Task ProtectAppointmentsAsync(int therapistId, IReadOnlyCollection<DayOfWeek> affectedDays,
        IReadOnlyList<TherapistSchedule> resultingWindows, CancellationToken cancellationToken)
    {
        var appointments = await _context.Appointments.AsNoTracking()
            .Where(appointment => appointment.TherapistId == therapistId).ToListAsync(cancellationToken);

        foreach (var appointment in appointments.Where(appointment => SchedulingRules.IsBlockingStatus(appointment.Status)
                     && affectedDays.Contains(appointment.StartDateTime.DayOfWeek)))
        {
            var occupiedEnd = SchedulingRules.GetOccupiedEnd(appointment.StartDateTime, appointment.EndDateTime);
            var appointmentDate = DateOnly.FromDateTime(appointment.StartDateTime);
            var contained = resultingWindows.Any(window => window.IsAvailable
                && window.DayOfWeek == appointment.StartDateTime.DayOfWeek && window.StartTime < window.EndTime
                && appointmentDate.ToDateTime(window.StartTime) <= appointment.StartDateTime
                && occupiedEnd <= appointmentDate.ToDateTime(window.EndTime));
            if (!contained)
            {
                throw new SchedulingConflictException(
                    "This schedule change would leave an existing Pending or Confirmed appointment, including its required break, outside an available schedule window.");
            }
        }
    }

    private static TherapistSchedule CreateWindow(int therapistId, ScheduleWindowRequestDto request) => new()
    {
        TherapistId = therapistId,
        DayOfWeek = request.DayOfWeek!.Value,
        StartTime = request.StartTime!.Value,
        EndTime = request.EndTime!.Value,
        IsAvailable = request.IsAvailable
    };

    private static ScheduleWindowDto ToDto(TherapistSchedule window) => new(window.ScheduleId,
        window.TherapistId, window.DayOfWeek, window.StartTime, window.EndTime, window.IsAvailable);

    private static void ValidateRequest(ScheduleWindowRequestDto request)
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), results, true))
        {
            var errors = results.SelectMany(result => (result.MemberNames.Any()
                    ? result.MemberNames : new[] { string.Empty })
                .Select(member => new { Member = member, Message = result.ErrorMessage ?? "Invalid schedule window." }))
                .GroupBy(error => error.Member).ToDictionary(group => group.Key,
                    group => group.Select(error => error.Message).Distinct().ToArray());
            throw new SchedulingValidationException(errors);
        }
    }

    private async Task<T> WriteAsync<T>(int therapistId, Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await WriteLock.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            if (_context.Database.IsSqlServer())
            {
                var lockName = $"PhysioGuide.TherapistSchedules.{therapistId}";
                await _context.Database.ExecuteSqlInterpolatedAsync($"""
                    DECLARE @result int;
                    EXEC @result = sys.sp_getapplock
                        @Resource = {lockName},
                        @LockMode = N'Exclusive',
                        @LockOwner = N'Transaction',
                        @LockTimeout = 15000;
                    IF @result < 0
                        THROW 51001, 'Therapist schedule lock could not be acquired.', 1;
                    """, cancellationToken);
            }

            var result = await operation();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new SchedulingConflictException("The schedule changed concurrently. Reload the schedule and try again.");
        }
        catch (DbUpdateException)
        {
            throw new SchedulingConflictException("The schedule could not be saved because related data changed. Reload the schedule and try again.");
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 2601 or 2627 or 3960 or 51001)
        {
            throw new SchedulingConflictException("The schedule could not be changed concurrently. Reload the schedule and try again.");
        }
        finally
        {
            WriteLock.Release();
        }
    }
}

public sealed class SchedulingValidationException : Exception
{
    public SchedulingValidationException(IDictionary<string, string[]> errors) : base("The schedule request is invalid.")
    {
        Errors = errors;
    }

    public IDictionary<string, string[]> Errors { get; }
}

public sealed class SchedulingConflictException : Exception
{
    public SchedulingConflictException(string message) : base(message)
    {
    }
}
