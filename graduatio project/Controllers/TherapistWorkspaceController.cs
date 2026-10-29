using System.Globalization;
using graduatio_project.Data;
using graduatio_project.DTOs.Therapist;
using graduatio_project.Models;
using graduatio_project.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace graduatio_project.Controllers
{
    //  مساحة المعالج: مواعيده ومرضاه ومحادثاته وجدوله
    //  الروابط:
    //    GET  /api/therapist/workspace
    //    PUT  /api/therapist/workspace/schedule
    //    PUT  /api/therapist/workspace/appointments/{appointmentId}/status
    //    GET  /api/therapist/workspace/patients
    //    GET/PUT /api/therapist/workspace/preferences
    [Route("api/therapist")]
    [ApiController]
    [Authorize(Roles = "Therapist")]
    public class TherapistWorkspaceController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public TherapistWorkspaceController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("workspace")]
        public async Task<IActionResult> GetWorkspace()
        {
            var resolved = await ResolveTherapistAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            var therapist = resolved.therapist!;
            var user = resolved.user!;
            var therapistId = therapist.TherapistId;

            var specialty = await _context.Specialties.AsNoTracking()
                .FirstOrDefaultAsync(s => s.SpecialtyId == therapist.SpecialtyId);

            var center = await _context.Centers.AsNoTracking()
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.CenterId == therapist.CenterId);

            var centerMapped = center == null
                ? null
                : (await CenterMapper.BuildAsync(_context, new[] { center }))[center.CenterId];

            var serviceLinks = await _context.TherapistServices.AsNoTracking()
                .Where(x => x.TherapistId == therapistId)
                .Select(x => x.ServiceId)
                .ToListAsync();

            var services = await _context.Services.AsNoTracking()
                .Include(s => s.Specialty)
                .Where(s => serviceLinks.Contains(s.ServiceId))
                .ToListAsync();

            var schedule = await _context.TherapistSchedules.AsNoTracking()
                .Where(s => s.TherapistId == therapistId)
                .OrderBy(s => s.DayOfWeek)
                .ToListAsync();

            var loaded = await AppointmentMapper.LoadAsync(_context,
                _context.Appointments.Where(a => a.TherapistId == therapistId));

            var patientIds = loaded.appointments
                .Select(a => a.PatientId)
                .Distinct()
                .ToList();

            var patients = await _context.Patients.AsNoTracking()
                .Include(p => p.User)
                .Where(p => patientIds.Contains(p.PatientId))
                .OrderBy(p => p.FullName)
                .ToListAsync();

            var appointmentCounts = loaded.appointments
                .GroupBy(a => a.PatientId)
                .ToDictionary(g => g.Key, g => g.Count());

            var notifications = await _context.Notifications.AsNoTracking()
                .Where(n => n.UserId == user.UserId)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();

            var conversations = await _context.Conversations.AsNoTracking()
                .Include(c => c.Patient)
                .Where(c => c.TherapistId == therapistId)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            var conversationIds = conversations.Select(c => c.ConversationId).ToList();
            var conversationMessages = conversationIds.Count == 0
                ? new List<Message>()
                : await _context.Messages.AsNoTracking()
                    .Where(m => conversationIds.Contains(m.ConversationId))
                    .OrderBy(m => m.SentAt)
                    .ToListAsync();

            var todayLocal = DateTime.Now.Date;
            var todayRows = loaded.appointments
                .Where(a => a.StartDateTime.Date == todayLocal)
                .OrderBy(a => a.StartDateTime)
                .ToList();

            var preferences = await _context.NotificationPreferences.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == user.UserId);

            return Ok(new
            {
                therapist = new
                {
                    id = therapist.TherapistId.ToString(),
                    name = therapist.FullName,
                    email = user.Email,
                    phone = user.PhoneNumber,
                    imageUrl = therapist.ProfileImageUrl,
                    bio = therapist.Bio,
                    specialtyId = therapist.SpecialtyId,
                    specialty = specialty == null
                        ? null
                        : CenterMapper.Pick(specialty.NameEnglish, specialty.NameArabic),
                    experience = therapist.YearsOfExperience ?? 0,
                    credentials = therapist.Qualifications,
                    languages = therapist.Languages,
                    availability = therapist.IsAvailable && therapist.IsActive
                        ? "available"
                        : "unavailable",
                    isActive = therapist.IsActive,
                    serviceIds = serviceLinks.Select(id => id.ToString()).ToList()
                },
                center = centerMapped,
                schedule = schedule.Select(s => new
                {
                    dayOfWeek = s.DayOfWeek.ToString().ToLowerInvariant(),
                    startTime = s.StartTime.ToString("HH:mm"),
                    endTime = s.EndTime.ToString("HH:mm"),
                    isAvailable = s.IsAvailable
                }).ToList(),
                services = services.Select(s => new
                {
                    id = s.ServiceId.ToString(),
                    name = CenterMapper.Pick(s.NameEnglish, s.NameArabic),
                    description = CenterMapper.Pick(s.DescriptionEnglish, s.DescriptionArabic),
                    price = s.Price,
                    duration = s.DurationMinutes,
                    isActive = s.IsActive
                }).ToList(),
                patients = patients.Select(p => new
                {
                    id = p.PatientId.ToString(),
                    name = p.FullName,
                    email = p.User?.Email,
                    phone = p.User?.PhoneNumber,
                    gender = p.Gender,
                    dateOfBirth = p.DateOfBirth?.ToString("yyyy-MM-dd"),
                    imageUrl = p.ProfileImageUrl,
                    appointmentCount = appointmentCounts.TryGetValue(p.PatientId, out var count)
                        ? count
                        : 0
                }).ToList(),
                appointments = AppointmentMapper.Build(
                    loaded.appointments, loaded.centers, loaded.therapists, loaded.services),
                notifications = notifications.Select(n => new
                {
                    id = n.NotificationId.ToString(),
                    type = n.Type,
                    title = n.Title,
                    message = n.Message,
                    isRead = n.IsRead,
                    date = n.CreatedAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                    relatedId = n.RelatedEntityId?.ToString()
                }).ToList(),
                conversations = conversations.Select(c => new
                {
                    id = c.ConversationId.ToString(),
                    patientId = c.PatientId.ToString(),
                    patientName = c.Patient?.FullName,
                    patientImage = c.Patient?.ProfileImageUrl,
                    lastMessage = conversationMessages
                        .LastOrDefault(m => m.ConversationId == c.ConversationId)?.MessageText,
                    unreadCount = conversationMessages.Count(m =>
                        m.ConversationId == c.ConversationId && !m.IsRead
                        && m.SenderUserId != user.UserId)
                }).ToList(),
                preferences = new
                {
                    emailEnabled = preferences?.EmailEnabled ?? true,
                    inAppEnabled = preferences?.InAppEnabled ?? true,
                    appointmentReminders = preferences?.AppointmentReminders ?? true,
                    marketing = preferences?.Marketing ?? false,
                    language = preferences?.Language ?? "en"
                },
                today = new
                {
                    date = todayLocal.ToString("yyyy-MM-dd"),
                    appointmentCount = todayRows.Count,
                    appointments = AppointmentMapper.Build(
                        todayRows, loaded.centers, loaded.therapists, loaded.services)
                }
            });
        }

        //  جدول المعالج: نضيف أو نحدّث يومًا واحدًا في كل طلب
        [HttpPut("workspace/schedule")]
        public async Task<IActionResult> UpdateSchedule(SaveScheduleDto dto)
        {
            var resolved = await ResolveTherapistAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            if (!Enum.TryParse<DayOfWeek>(dto.DayOfWeek, true, out var dayOfWeek))
            {
                return ApiError.Validation(new Dictionary<string, string[]>
                {
                    ["DayOfWeek"] = new[] { "DayOfWeek must be a valid day name." }
                }, "DayOfWeek must be a valid day name.");
            }

            var errors = new Dictionary<string, string[]>();

            if (!TimeOnly.TryParseExact(dto.StartTime, "HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var start))
            {
                errors["StartTime"] = new[] { "Use HH:MM." };
            }

            if (!TimeOnly.TryParseExact(dto.EndTime, "HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var end))
            {
                errors["EndTime"] = new[] { "Use HH:MM." };
            }

            if (errors.Count > 0)
            {
                return ApiError.Validation(errors);
            }

            if (end <= start)
            {
                return ApiError.Validation(new Dictionary<string, string[]>
                {
                    ["EndTime"] = new[] { "endTime must be after startTime." }
                }, "endTime must be after startTime.");
            }

            var schedule = await _context.TherapistSchedules
                .FirstOrDefaultAsync(s => s.TherapistId == resolved.therapist!.TherapistId
                    && s.DayOfWeek == dayOfWeek);

            if (schedule == null)
            {
                schedule = new TherapistSchedule
                {
                    TherapistId = resolved.therapist!.TherapistId,
                    DayOfWeek = dayOfWeek
                };
                _context.TherapistSchedules.Add(schedule);
            }

            schedule.StartTime = start;
            schedule.EndTime = end;
            schedule.IsAvailable = dto.IsAvailable;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                dayOfWeek = schedule.DayOfWeek.ToString().ToLowerInvariant(),
                startTime = schedule.StartTime.ToString("HH:mm"),
                endTime = schedule.EndTime.ToString("HH:mm"),
                isAvailable = schedule.IsAvailable
            });
        }

        [HttpPut("workspace/appointments/{appointmentId:int}/status")]
        public async Task<IActionResult> UpdateAppointmentStatus(
            int appointmentId, TherapistAppointmentStatusDto dto)
        {
            var resolved = await ResolveTherapistAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            var appointment = await _context.Appointments
                .FirstOrDefaultAsync(a => a.AppointmentId == appointmentId
                    && a.TherapistId == resolved.therapist!.TherapistId);

            if (appointment == null)
            {
                return ApiError.NotFound("not_found", "Appointment not found.");
            }

            AppointmentStatusRules.Apply(appointment, dto.Status, dto.Reason);
            await _context.SaveChangesAsync();

            var loaded = await AppointmentMapper.LoadAsync(_context,
                _context.Appointments.Where(a => a.AppointmentId == appointmentId));

            return Ok(AppointmentMapper.Build(
                loaded.appointments, loaded.centers, loaded.therapists, loaded.services)[0]);
        }

        //  المرضى الذين لديهم موعد مع هذا المعالج فقط
        [HttpGet("workspace/patients")]
        public async Task<IActionResult> GetPatients()
        {
            var resolved = await ResolveTherapistAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            var therapistId = resolved.therapist!.TherapistId;

            var patientIds = await _context.Appointments.AsNoTracking()
                .Where(a => a.TherapistId == therapistId)
                .Select(a => a.PatientId)
                .Distinct()
                .ToListAsync();

            var patients = await _context.Patients.AsNoTracking()
                .Include(p => p.User)
                .Where(p => patientIds.Contains(p.PatientId))
                .OrderBy(p => p.FullName)
                .ToListAsync();

            return Ok(patients.Select(p => new
            {
                id = p.PatientId.ToString(),
                name = p.FullName,
                email = p.User?.Email,
                phone = p.User?.PhoneNumber,
                gender = p.Gender,
                dateOfBirth = p.DateOfBirth?.ToString("yyyy-MM-dd"),
                imageUrl = p.ProfileImageUrl
            }).ToList());
        }

        [HttpGet("workspace/preferences")]
        public async Task<IActionResult> GetPreferences()
        {
            if (!CurrentUser.TryGetUserId(User, out var userId))
            {
                return ApiError.Unauthorized("unauthenticated", "Authentication is required.");
            }

            var preferences = await _context.NotificationPreferences.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId);

            return Ok(new
            {
                emailEnabled = preferences?.EmailEnabled ?? true,
                inAppEnabled = preferences?.InAppEnabled ?? true,
                appointmentReminders = preferences?.AppointmentReminders ?? true,
                marketing = preferences?.Marketing ?? false,
                language = preferences?.Language ?? "en"
            });
        }

        [HttpPut("workspace/preferences")]
        public async Task<IActionResult> UpdatePreferences(DTOs.Patient.UpdatePreferencesDto dto)
        {
            if (!CurrentUser.TryGetUserId(User, out var userId))
            {
                return ApiError.Unauthorized("unauthenticated", "Authentication is required.");
            }

            var preferences = await _context.NotificationPreferences
                .FirstOrDefaultAsync(p => p.UserId == userId);

            if (preferences == null)
            {
                preferences = new NotificationPreference { UserId = userId };
                _context.NotificationPreferences.Add(preferences);
            }

            preferences.EmailEnabled = dto.EmailEnabled;
            preferences.InAppEnabled = dto.InAppEnabled;
            preferences.AppointmentReminders = dto.AppointmentReminders;
            preferences.Marketing = dto.Marketing;
            preferences.Language = dto.Language.ToLowerInvariant();

            await _context.SaveChangesAsync();

            return Ok(new
            {
                emailEnabled = preferences.EmailEnabled,
                inAppEnabled = preferences.InAppEnabled,
                appointmentReminders = preferences.AppointmentReminders,
                marketing = preferences.Marketing,
                language = preferences.Language
            });
        }

        private async Task<(Therapist? therapist, User? user, IActionResult? error)>
            ResolveTherapistAsync()
        {
            if (!CurrentUser.TryGetUserId(User, out var userId))
            {
                return (null, null, ApiError.Unauthorized(
                    "unauthenticated", "Authentication is required."));
            }

            var user = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == userId);
            var therapist = await _context.Therapists.AsNoTracking()
                .FirstOrDefaultAsync(t => t.UserId == userId);

            if (user == null || therapist == null)
            {
                return (null, null, ApiError.NotFound(
                    "not_found", "Therapist profile not found."));
            }

            return (therapist, user, null);
        }
    }
}