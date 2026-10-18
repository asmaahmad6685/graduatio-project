namespace graduatio_project.DTOs.Appointments;

public sealed record AppointmentResponseDto(
    int AppointmentId,
    AppointmentPatientDto Patient,
    AppointmentCenterDto Center,
    AppointmentTherapistDto Therapist,
    AppointmentServiceDto Service,
    DateTime StartDateTime,
    DateTime EndDateTime,
    DateTime ReservedUntil,
    decimal PriceAtBooking,
    string? PatientNotes,
    string Status,
    string? RejectionReason,
    string? CancellationReason,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string TimeZone);

public sealed record AppointmentPatientDto(int PatientId, string FullName);

public sealed record AppointmentCenterDto(int CenterId, string NameArabic, string? NameEnglish);

public sealed record AppointmentTherapistDto(int TherapistId, string FullName);

public sealed record AppointmentServiceDto(int ServiceId, string NameArabic, string? NameEnglish);
