using System.Globalization;
using graduatio_project.Data;
using graduatio_project.DTOs.Appointments;
using graduatio_project.Models;
using graduatio_project.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace graduatio_project.Controllers
{
    //  حجز موعد جديد من لوحة المريض
    //  الرابط: POST /api/appointments/request
    [Route("api/appointments")]
    [ApiController]
    [Authorize]
    public class AppointmentsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly NotificationService _notifications;

        public AppointmentsController(ApplicationDbContext context, NotificationService notifications)
        {
            _context = context;
            _notifications = notifications;
        }

        [HttpPost("request")]
        public async Task<IActionResult> RequestAppointment(AppointmentRequestDto dto)
        {
            //  هذا المسار مخصص للمريض فقط
            if (!CurrentUser.IsRole(User, "Patient"))
            {
                return ApiError.Forbidden("forbidden", "Only patients can request appointments.");
            }

            if (!CurrentUser.TryGetUserId(User, out var userId))
            {
                return ApiError.Unauthorized("unauthenticated", "Authentication is required.");
            }

            var patient = await _context.Patients.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId);

            if (patient == null)
            {
                return ApiError.NotFound("not_found", "Patient profile not found.");
            }

            //  التاريخ والوقت يمرّان بنفس التحقق الذي تفرضه الواجهة
            var errors = new Dictionary<string, string[]>();

            if (!DateOnly.TryParseExact(dto.Date, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                errors["Date"] = new[] { "Date must be in YYYY-MM-DD format." };
            }

            if (!TimeOnly.TryParseExact(dto.Time, "HH:mm",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                errors["Time"] = new[] { "Time must be in HH:MM 24-hour format." };
            }

            if (errors.Count > 0)
            {
                return ApiError.Validation(errors);
            }

            //  المركز لازل معتمد والمعالج والخدمة يخصّانه ويقدّمها المعالج
            var (target, code, message) = await BookingRules.ResolveAsync(
                _context, dto.CenterId, dto.TherapistId, dto.ServiceId);

            if (target == null)
            {
                return ApiError.NotFound(code, message);
            }

            var now = DateTime.Now;
            var start = day.ToDateTime(time);

            //  الحجز في الماضي أو في موعد لم يعد متاحًا = تعارض وليس خطأ تحقق
            if (start <= now)
            {
                return ApiError.Conflict("slot_unavailable",
                    "That time slot is not available.");
            }

            //  نعيد تشغيل حساب المواعيد المتاحة نفسها بالضبط
            var available = await BookingRules.IsSlotAvailableAsync(
                _context, dto.CenterId, dto.TherapistId, dto.ServiceId, day, time, now);

            if (!available)
            {
                return ApiError.Conflict("slot_unavailable",
                    "That time slot is no longer available.");
            }

            var appointment = new Appointment
            {
                PatientId = patient.PatientId,
                CenterId = target.Center.CenterId,
                TherapistId = target.Therapist.TherapistId,
                ServiceId = target.Service.ServiceId,
                StartDateTime = start,
                EndDateTime = start.AddMinutes(target.Service.DurationMinutes),
                PriceAtBooking = target.Service.Price,
                Status = "Pending",
                PatientNotes = dto.Notes,
                CreatedAt = DateTime.UtcNow
            };

            _context.Appointments.Add(appointment);
            await _context.SaveChangesAsync();

            //  الجرس: المعالج والمركز يُعلمون بطلب الموعد الجديد
            var therapistUserId = await _context.Therapists.AsNoTracking()
                .Where(t => t.TherapistId == appointment.TherapistId)
                .Select(t => t.UserId)
                .FirstOrDefaultAsync();
            var centerUserId = await _context.Centers.AsNoTracking()
                .Where(c => c.CenterId == appointment.CenterId)
                .Select(c => c.UserId)
                .FirstOrDefaultAsync();
            await _notifications.NotifyAsync(
                new[] { therapistUserId, centerUserId },
                "appointmentRequest",
                "New appointment request",
                $"A patient requested an appointment on {day:yyyy-MM-dd} at {time:HH:mm}.",
                appointment.AppointmentId);

            var payload = AppointmentMapper.Build(
                new[] { appointment },
                new Dictionary<int, Center> { [appointment.CenterId] = target.Center },
                new Dictionary<int, Therapist> { [appointment.TherapistId] = target.Therapist },
                new Dictionary<int, Service> { [appointment.ServiceId] = target.Service });

            return StatusCode(StatusCodes.Status201Created, payload[0]);
        }
    }
}




// **************************** Shahd Work ***********************************




// using graduatio_project.DTOs.Appointments;
// using graduatio_project.Services;
// using Microsoft.AspNetCore.Authorization;
// using Microsoft.AspNetCore.Mvc;
// using Microsoft.AspNetCore.Mvc.ModelBinding;

// namespace graduatio_project.Controllers;

// [Route("api/[controller]")]
// [ApiController]
// [Authorize(Roles = "Patient")]
// public sealed class AppointmentsController(AppointmentService service) : AppointmentControllerBase
// {
//     [HttpPost]
//     public Task<IActionResult> Create(CreateAppointmentDto request, CancellationToken cancellationToken) =>
//         ExecuteAsync(userId => service.CreateAsync(userId, request, cancellationToken),
//             appointment => CreatedAtAction(nameof(Get), new { appointmentId = appointment.AppointmentId }, appointment));

//     [HttpGet]
//     public Task<IActionResult> List(CancellationToken cancellationToken) =>
//         ExecuteAsync(userId => service.ListPatientAsync(userId, cancellationToken));

//     [HttpGet("{appointmentId:int}")]
//     public Task<IActionResult> Get(int appointmentId, CancellationToken cancellationToken) =>
//         ExecuteAsync(userId => service.GetPatientAsync(userId, appointmentId, cancellationToken));

//     [HttpPatch("{appointmentId:int}/cancel")]
//     public Task<IActionResult> Cancel(int appointmentId,
//         [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancellationReasonDto? request,
//         CancellationToken cancellationToken) =>
//         ExecuteAsync(userId => service.CancelPatientAsync(userId, appointmentId,
//             request ?? new CancellationReasonDto(), cancellationToken));

//     [HttpPatch("{appointmentId:int}/reschedule")]
//     public Task<IActionResult> Reschedule(int appointmentId, RescheduleAppointmentDto request,
//         CancellationToken cancellationToken) =>
//         ExecuteAsync(userId => service.RescheduleAsync(userId, appointmentId, request, cancellationToken));
// }
