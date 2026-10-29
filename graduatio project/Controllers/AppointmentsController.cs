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

        public AppointmentsController(ApplicationDbContext context)
        {
            _context = context;
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

            var payload = AppointmentMapper.Build(
                new[] { appointment },
                new Dictionary<int, Center> { [appointment.CenterId] = target.Center },
                new Dictionary<int, Therapist> { [appointment.TherapistId] = target.Therapist },
                new Dictionary<int, Service> { [appointment.ServiceId] = target.Service });

            return StatusCode(StatusCodes.Status201Created, payload[0]);
        }
    }
}