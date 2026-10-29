using System.ComponentModel.DataAnnotations;
using graduatio_project.Data;
using graduatio_project.DTOs.Appointments;
using graduatio_project.DTOs.Availability;
using graduatio_project.Models;
using Microsoft.EntityFrameworkCore;

namespace graduatio_project.Services;

public sealed class AppointmentService(
    ApplicationDbContext context,
    AvailabilityService availability,
    SchedulingClock clock,
    TimeProvider timeProvider,
    SchedulingWriteCoordinator writes)
{
    private readonly record struct Owner(int UserId, int ProfileId, bool IsCenter);

    public async Task<AppointmentResponseDto> CreateAsync(int userId, CreateAppointmentDto request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var owner = await ResolveOwnerAsync(userId, false, cancellationToken);
        await CompleteExpiredAsync(owner, null, cancellationToken);
        return await writes.ExecuteAsync([request.TherapistId!.Value], [owner.ProfileId], async () =>
        {
            await RevalidateOwnerAsync(owner, cancellationToken);
            var service = await ValidateSelectionAsync(request.CenterId!.Value, request.ServiceId!.Value,
                request.TherapistId.Value, cancellationToken);
            var slot = await RequireSlotAsync(request.TherapistId.Value, service.ServiceId,
                request.StartDateTime!.Value, null, cancellationToken);
            await ValidatePatientConflictAsync(owner.ProfileId, slot, null, cancellationToken);
            ValidateCurrentStart(slot.StartDateTime);

            var appointment = new Appointment
            {
                PatientId = owner.ProfileId,
                CenterId = request.CenterId.Value,
                TherapistId = request.TherapistId.Value,
                ServiceId = service.ServiceId,
                StartDateTime = slot.StartDateTime,
                EndDateTime = slot.EndDateTime,
                PriceAtBooking = service.Price,
                PatientNotes = request.PatientNotes?.Trim(),
                Status = "Pending",
                CreatedAt = UtcNow
            };
            context.Appointments.Add(appointment);
            context.AppointmentStatusHistories.Add(new AppointmentStatusHistory
            {
                Appointment = appointment,
                OldStatus = null,
                NewStatus = "Pending",
                ChangedByUserId = userId,
                ChangedAt = appointment.CreatedAt
            });
            await SaveAsync(appointment, cancellationToken);
            return await ReadResponseAsync(owner, appointment.AppointmentId, cancellationToken);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<AppointmentResponseDto>> ListPatientAsync(int userId,
        CancellationToken cancellationToken = default) => ListAsync(userId, false, cancellationToken);

    public Task<IReadOnlyList<AppointmentResponseDto>> ListCenterAsync(int userId,
        CancellationToken cancellationToken = default) => ListAsync(userId, true, cancellationToken);

    public Task<AppointmentResponseDto> GetPatientAsync(int userId, int appointmentId,
        CancellationToken cancellationToken = default) => GetAsync(userId, appointmentId, false, cancellationToken);

    public Task<AppointmentResponseDto> GetCenterAsync(int userId, int appointmentId,
        CancellationToken cancellationToken = default) => GetAsync(userId, appointmentId, true, cancellationToken);

    public async Task<AppointmentResponseDto> CancelPatientAsync(int userId, int appointmentId,
        CancellationReasonDto request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        var owner = await ResolveOwnerAsync(userId, false, cancellationToken);
        await CompleteExpiredAsync(owner, appointmentId, cancellationToken);
        return await WriteOwnedAsync(owner, appointmentId, null, async appointment =>
        {
            RequirePatientEditable(appointment);
            appointment.CancellationReason = request.CancellationReason?.Trim();
            AddTransition(appointment, "Cancelled", userId);
            context.Appointments.Update(appointment);
            await SaveAsync(appointment, cancellationToken);
            return await ReadResponseAsync(owner, appointmentId, cancellationToken);
        }, cancellationToken);
    }

    public async Task<AppointmentResponseDto> RescheduleAsync(int userId, int appointmentId,
        RescheduleAppointmentDto request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        var owner = await ResolveOwnerAsync(userId, false, cancellationToken);
        await CompleteExpiredAsync(owner, appointmentId, cancellationToken);
        return await WriteOwnedAsync(owner, appointmentId, request.TherapistId!.Value, async appointment =>
        {
            RequirePatientEditable(appointment);
            await ValidateSelectionAsync(appointment.CenterId, appointment.ServiceId,
                request.TherapistId.Value, cancellationToken);
            var slot = await RequireSlotAsync(request.TherapistId.Value, appointment.ServiceId,
                request.StartDateTime!.Value, appointmentId, cancellationToken);
            await ValidatePatientConflictAsync(appointment.PatientId, slot, appointmentId, cancellationToken);
            RequirePatientEditable(appointment);
            ValidateCurrentStart(slot.StartDateTime);

            appointment.TherapistId = request.TherapistId.Value;
            appointment.StartDateTime = slot.StartDateTime;
            appointment.EndDateTime = slot.EndDateTime;
            appointment.UpdatedAt = UtcNow;
            context.Appointments.Update(appointment);
            await SaveAsync(appointment, cancellationToken);
            return await ReadResponseAsync(owner, appointmentId, cancellationToken);
        }, cancellationToken);
    }

    public Task<AppointmentResponseDto> ConfirmAsync(int userId, int appointmentId,
        CancellationToken cancellationToken = default) =>
        ChangeCenterStatusAsync(userId, appointmentId, "Confirmed", null, cancellationToken);

    public Task<AppointmentResponseDto> RejectAsync(int userId, int appointmentId,
        RejectionReasonDto request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        return ChangeCenterStatusAsync(userId, appointmentId, "Rejected", request.RejectionReason?.Trim(), cancellationToken);
    }

    public Task<AppointmentResponseDto> CancelCenterAsync(int userId, int appointmentId,
        CancellationReasonDto request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        return ChangeCenterStatusAsync(userId, appointmentId, "Cancelled", request.CancellationReason?.Trim(), cancellationToken);
    }

    private async Task<AppointmentResponseDto> ChangeCenterStatusAsync(int userId, int appointmentId,
        string newStatus, string? reason, CancellationToken cancellationToken)
    {
        var owner = await ResolveOwnerAsync(userId, true, cancellationToken);
        // Completion is committed before an action, including an invalid action on an expired booking.
        await CompleteExpiredAsync(owner, appointmentId, cancellationToken);
        return await WriteOwnedAsync(owner, appointmentId, null, async appointment =>
        {
            var pending = IsStatus(appointment, "Pending");
            if (!pending && !(newStatus == "Cancelled" && IsStatus(appointment, "Confirmed")))
                throw new AppointmentConflictException("This appointment status does not allow the requested transition.");

            if (newStatus == "Rejected") appointment.RejectionReason = reason;
            if (newStatus == "Cancelled") appointment.CancellationReason = reason;
            AddTransition(appointment, newStatus, userId);
            // No extra Center deadline is imposed. An already-ended Pending booking can be
            // confirmed, and immediately satisfies the approved automatic completion rule.
            if (newStatus == "Confirmed" && appointment.EndDateTime < clock.Now)
                AddTransition(appointment, "Completed", null);
            context.Appointments.Update(appointment);
            await SaveAsync(appointment, cancellationToken);
            return await ReadResponseAsync(owner, appointmentId, cancellationToken);
        }, cancellationToken);
    }

    private async Task<IReadOnlyList<AppointmentResponseDto>> ListAsync(int userId, bool isCenter,
        CancellationToken cancellationToken)
    {
        var owner = await ResolveOwnerAsync(userId, isCenter, cancellationToken);
        await CompleteExpiredAsync(owner, null, cancellationToken);
        var appointments = await WithDetails(Owned(owner)).OrderByDescending(value => value.StartDateTime)
            .ThenByDescending(value => value.AppointmentId).ToListAsync(cancellationToken);
        return appointments.Select(ToDto).ToList();
    }

    private async Task<AppointmentResponseDto> GetAsync(int userId, int appointmentId, bool isCenter,
        CancellationToken cancellationToken)
    {
        var owner = await ResolveOwnerAsync(userId, isCenter, cancellationToken);
        await CompleteExpiredAsync(owner, appointmentId, cancellationToken);
        return await ReadResponseAsync(owner, appointmentId, cancellationToken);
    }

    private async Task<Owner> ResolveOwnerAsync(int userId, bool isCenter, CancellationToken cancellationToken)
    {
        if (userId <= 0) throw new AppointmentAccessException(401, "Invalid token identity.");
        var user = await context.Users.AsNoTracking().SingleOrDefaultAsync(value => value.UserId == userId, cancellationToken);
        if (user == null) throw new AppointmentAccessException(401, "Invalid token identity.");
        if (!user.IsActive || !string.Equals(user.Role, isCenter ? "Center" : "Patient", StringComparison.Ordinal))
            throw new AppointmentAccessException(403, "This account is not permitted to perform appointment actions.");

        if (isCenter)
        {
            var center = await context.Centers.AsNoTracking().SingleOrDefaultAsync(value => value.UserId == userId, cancellationToken);
            if (center == null) throw new AppointmentAccessException(404, "Center profile not found.");
            if (!SchedulingRules.IsBookableCenter(center.Status))
                throw new AppointmentAccessException(403, "The Center must be Active to access appointment operations.");
            return new(userId, center.CenterId, true);
        }

        var patientId = await context.Patients.AsNoTracking().Where(value => value.UserId == userId)
            .Select(value => (int?)value.PatientId).SingleOrDefaultAsync(cancellationToken);
        if (patientId == null) throw new AppointmentAccessException(404, "Patient profile not found.");
        return new(userId, patientId.Value, false);
    }

    private async Task RevalidateOwnerAsync(Owner owner, CancellationToken cancellationToken)
    {
        var current = await ResolveOwnerAsync(owner.UserId, owner.IsCenter, cancellationToken);
        if (current != owner) throw new AppointmentAccessException(404, "Appointment owner profile not found.");
    }

    private IQueryable<Appointment> Owned(Owner owner) => context.Appointments.AsNoTracking().Where(value =>
        owner.IsCenter ? value.CenterId == owner.ProfileId : value.PatientId == owner.ProfileId);

    private async Task<Appointment> FindOwnedAsync(Owner owner, int appointmentId, CancellationToken cancellationToken) =>
        await Owned(owner).SingleOrDefaultAsync(value => value.AppointmentId == appointmentId, cancellationToken)
        ?? throw new AppointmentAccessException(404, "Appointment not found.");

    private async Task<T> WriteOwnedAsync<T>(Owner owner, int appointmentId, int? newTherapistId,
        Func<Appointment, Task<T>> operation, CancellationToken cancellationToken, bool completeBeforeOperation = true)
    {
        // The first read only identifies locks. Re-read ownership and mutable data after acquiring them.
        var snapshot = await FindOwnedAsync(owner, appointmentId, cancellationToken);
        var therapistIds = newTherapistId.HasValue
            ? new[] { snapshot.TherapistId, newTherapistId.Value } : new[] { snapshot.TherapistId };
        var result = await writes.ExecuteAsync(therapistIds, [snapshot.PatientId], async () =>
        {
            await RevalidateOwnerAsync(owner, cancellationToken);
            var appointment = await FindOwnedAsync(owner, appointmentId, cancellationToken);
            if (appointment.TherapistId != snapshot.TherapistId || appointment.PatientId != snapshot.PatientId)
                throw new AppointmentConflictException("The appointment changed concurrently. Reload it and try again.");
            // The end may have passed while waiting for the lock. Commit automatic completion
            // before reporting an invalid requested transition, so it cannot be rolled back.
            if (completeBeforeOperation && IsStatus(appointment, "Confirmed") && appointment.EndDateTime < clock.Now)
            {
                AddTransition(appointment, "Completed", null);
                context.Appointments.Update(appointment);
                await SaveAsync(appointment, cancellationToken);
                return (Completed: true, Value: default(T));
            }
            return (Completed: false, Value: await operation(appointment));
        }, cancellationToken);
        if (result.Completed)
            throw new AppointmentConflictException("The appointment has completed and does not allow the requested transition.");
        return result.Value!;
    }

    private async Task CompleteExpiredAsync(Owner owner, int? appointmentId, CancellationToken cancellationToken)
    {
        // Completion is scoped to the caller's appointments, never a foreign resource supplied by ID.
        if (appointmentId.HasValue) await FindOwnedAsync(owner, appointmentId.Value, cancellationToken);
        var now = clock.Now;
        var expiredIds = await Owned(owner).Where(value => value.Status.ToUpper() == "CONFIRMED"
                && value.EndDateTime < now && (!appointmentId.HasValue || value.AppointmentId == appointmentId.Value))
            .Select(value => value.AppointmentId).ToListAsync(cancellationToken);
        foreach (var id in expiredIds)
        {
            await WriteOwnedAsync(owner, id, null, async appointment =>
            {
                if (IsStatus(appointment, "Confirmed") && appointment.EndDateTime < clock.Now)
                {
                    AddTransition(appointment, "Completed", null);
                    context.Appointments.Update(appointment);
                    await SaveAsync(appointment, cancellationToken);
                }
                return true;
            }, cancellationToken, completeBeforeOperation: false);
        }
    }

    private async Task<Service> ValidateSelectionAsync(int centerId, int serviceId, int therapistId,
        CancellationToken cancellationToken)
    {
        var center = await context.Centers.AsNoTracking().SingleOrDefaultAsync(value => value.CenterId == centerId, cancellationToken);
        if (center == null) throw new AppointmentAccessException(404, "Center not found.");
        if (!SchedulingRules.IsBookableCenter(center.Status))
            throw new AppointmentConflictException("The selected Center is not bookable.");
        var therapist = await context.Therapists.AsNoTracking().SingleOrDefaultAsync(value => value.TherapistId == therapistId, cancellationToken);
        if (therapist == null) throw new AppointmentAccessException(404, "Therapist not found.");
        var service = await context.Services.AsNoTracking().SingleOrDefaultAsync(value => value.ServiceId == serviceId, cancellationToken);
        if (service == null) throw new AppointmentAccessException(404, "Service not found.");
        if (therapist.CenterId != centerId || service.CenterId != centerId || !therapist.IsActive
            || !therapist.IsAvailable || !service.IsActive
            || !await context.TherapistServices.AsNoTracking().AnyAsync(value => value.TherapistId == therapistId
                && value.ServiceId == serviceId, cancellationToken))
            throw new AppointmentConflictException("The selected Therapist and Service are not bookable at this Center.");
        return service;
    }

    private async Task<AvailabilitySlotDto> RequireSlotAsync(int therapistId, int serviceId, DateTime start,
        int? excludedAppointmentId, CancellationToken cancellationToken)
    {
        if (start.Kind != DateTimeKind.Unspecified)
            throw new AppointmentValidationException("StartDateTime must be a local Asia/Hebron date/time without an offset or Z suffix.");
        try
        {
            var response = await availability.GetForBookingAsync(therapistId, serviceId,
                DateOnly.FromDateTime(start), excludedAppointmentId, cancellationToken);
            return response.Slots.SingleOrDefault(value => value.StartDateTime == start)
                ?? throw new AppointmentConflictException("The requested start is not a currently bookable slot.");
        }
        catch (AvailabilityValidationException exception)
        {
            throw new AppointmentValidationException(exception.Message);
        }
        catch (AvailabilityNotFoundException exception)
        {
            throw new AppointmentAccessException(404, exception.Message);
        }
    }

    private async Task ValidatePatientConflictAsync(int patientId, AvailabilitySlotDto slot,
        int? excludedAppointmentId, CancellationToken cancellationToken)
    {
        var appointments = await context.Appointments.AsNoTracking().Where(value => value.PatientId == patientId
                && (!excludedAppointmentId.HasValue || value.AppointmentId != excludedAppointmentId.Value)
                && (value.Status.ToUpper() == "PENDING" || value.Status.ToUpper() == "CONFIRMED")
                && value.StartDateTime < slot.ReservedUntil)
            .Select(value => new { value.StartDateTime, value.EndDateTime }).ToListAsync(cancellationToken);
        if (appointments.Any(value => slot.StartDateTime < SchedulingRules.GetOccupiedEnd(value.StartDateTime, value.EndDateTime)
            && value.StartDateTime < slot.ReservedUntil))
            throw new AppointmentConflictException("The Patient already has an overlapping Pending or Confirmed appointment.");
    }

    private void ValidateCurrentStart(DateTime start)
    {
        var now = clock.Now;
        var today = DateOnly.FromDateTime(now);
        var date = DateOnly.FromDateTime(start);
        if (start < now || date < today || date > today.AddMonths(1))
            throw new AppointmentValidationException("StartDateTime must be a current or future slot within one calendar month.");
    }

    private void RequirePatientEditable(Appointment appointment)
    {
        if (!IsStatus(appointment, "Pending") || appointment.StartDateTime <= clock.Now)
            throw new AppointmentConflictException("Patients may only cancel or reschedule a Pending appointment that has not started.");
    }

    private static bool IsStatus(Appointment appointment, string status) =>
        string.Equals(appointment.Status, status, StringComparison.OrdinalIgnoreCase);

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    private void AddTransition(Appointment appointment, string newStatus, int? changedByUserId)
    {
        var now = UtcNow;
        context.AppointmentStatusHistories.Add(new AppointmentStatusHistory
        {
            AppointmentId = appointment.AppointmentId,
            OldStatus = appointment.Status,
            NewStatus = newStatus,
            ChangedByUserId = changedByUserId,
            ChangedAt = now
        });
        appointment.Status = newStatus;
        appointment.UpdatedAt = now;
    }

    private async Task SaveAsync(Appointment appointment, CancellationToken cancellationToken)
    {
        await context.SaveChangesAsync(cancellationToken);
        context.Entry(appointment).State = EntityState.Detached;
    }

    private static IQueryable<Appointment> WithDetails(IQueryable<Appointment> query) =>
        query.Include(value => value.Patient).Include(value => value.Center)
            .Include(value => value.Therapist).Include(value => value.Service);

    private async Task<AppointmentResponseDto> ReadResponseAsync(Owner owner, int appointmentId, CancellationToken cancellationToken)
    {
        var appointment = await WithDetails(Owned(owner)).SingleOrDefaultAsync(value => value.AppointmentId == appointmentId, cancellationToken)
            ?? throw new AppointmentAccessException(404, "Appointment not found.");
        return ToDto(appointment);
    }

    private static AppointmentResponseDto ToDto(Appointment value) => new(
        value.AppointmentId,
        new(value.PatientId, value.Patient.FullName),
        new(value.CenterId, value.Center.NameArabic, value.Center.NameEnglish),
        new(value.TherapistId, value.Therapist.FullName),
        new(value.ServiceId, value.Service.NameArabic, value.Service.NameEnglish),
        DateTime.SpecifyKind(value.StartDateTime, DateTimeKind.Unspecified),
        DateTime.SpecifyKind(value.EndDateTime, DateTimeKind.Unspecified),
        SchedulingRules.GetOccupiedEnd(value.StartDateTime, value.EndDateTime),
        value.PriceAtBooking, value.PatientNotes, value.Status, value.RejectionReason, value.CancellationReason,
        DateTime.SpecifyKind(value.CreatedAt, DateTimeKind.Utc),
        value.UpdatedAt.HasValue ? DateTime.SpecifyKind(value.UpdatedAt.Value, DateTimeKind.Utc) : null,
        SchedulingClock.TimeZoneId);

    private static void Validate(object request)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), errors, true))
            throw new AppointmentValidationException(string.Join(" ", errors.Select(value => value.ErrorMessage)));
    }
}

public sealed class AppointmentAccessException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class AppointmentValidationException(string message) : Exception(message);
public sealed class AppointmentConflictException(string message) : Exception(message);
