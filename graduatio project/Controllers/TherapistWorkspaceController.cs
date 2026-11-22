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
    //    GET/PUT /api/therapist/workspace/preferences
    [Route("api/therapist")]
    [ApiController]
    [Authorize(Roles = "Therapist")]
    public class TherapistWorkspaceController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly NotificationService _notifications;

        public TherapistWorkspaceController(
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            NotificationService notifications)
        {
            _context = context;
            _environment = environment;
            _notifications = notifications;
        }

        //  رفع مرفق رسالة: نفس عقد صورة المريض (حقل file، ٢ ميجابايت، { url })
        [HttpPost("workspace/images")]
        [RequestSizeLimit(ImageStorage.MaxBytes + 1024 * 1024)]
        public async Task<IActionResult> UploadImage(IFormFile? file)
        {
            var validation = ImageStorage.Validate(file);
            if (validation != null)
            {
                return validation;
            }

            var url = await ImageStorage.SaveAsync(file!, _environment);
            return Ok(new { url });
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
                    nameAr = therapist.FullNameAr,
                    email = user.Email,
                    phone = user.PhoneNumber,
                    imageUrl = therapist.ProfileImageUrl,
                    bio = therapist.Bio,
                    bioAr = therapist.BioAr,
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