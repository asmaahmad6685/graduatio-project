using System.Globalization;
using graduatio_project.Data;
using graduatio_project.DTOs.Patient;
using graduatio_project.Models;
using graduatio_project.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace graduatio_project.Controllers
{
    //  لوحة المريض: المواعيد، مساحة الرسائل، الحساب
    //  الروابط:
    //    GET    /api/patient/appointments
    //    GET    /api/patient/messaging-workspace?since=<ISO>
    //    GET    /api/patient/account
    //    PUT    /api/patient/account/profile
    //    PUT    /api/patient/account/preferences
    //    POST   /api/patient/account/image
    [Route("api/patient")]
    [ApiController]
    [Authorize(Roles = "Patient")]
    public class PatientController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public PatientController(ApplicationDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        //  المواعيد الخاصة بهذا المريض فقط — لا يرى مواعيد غيره مهما كانت الطلب
        [HttpGet("appointments")]
        public async Task<IActionResult> GetAppointments(
            [FromQuery] string? status,
            [FromQuery] string? from,
            [FromQuery] string? to)
        {
            var resolved = await ResolvePatientAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            IQueryable<Appointment> query = _context.Appointments
                .Where(a => a.PatientId == resolved.patient!.PatientId);

            if (!string.IsNullOrWhiteSpace(status))
            {
                var normalized = status.Trim().ToLowerInvariant();
                query = query.Where(a => a.Status.ToLower() == normalized);
            }

            if (DateOnly.TryParseExact(from, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromDay))
            {
                query = query.Where(a => a.StartDateTime >= fromDay.ToDateTime(TimeOnly.MinValue));
            }

            if (DateOnly.TryParseExact(to, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var toDay))
            {
                query = query.Where(a => a.StartDateTime < toDay.ToDateTime(TimeOnly.MinValue).AddDays(1));
            }

            var loaded = await AppointmentMapper.LoadAsync(_context, query);
            return Ok(AppointmentMapper.Build(
                loaded.appointments, loaded.centers, loaded.therapists, loaded.services));
        }

        //  مساحة الرسائل: كل ما تحتاجه الشاشة في استجابة واحدة
        //  since (اختياري) يعيد فقط ما جديد عليه حتى تستطيع الواجهة الاستطلاع
        [HttpGet("messaging-workspace")]
        public async Task<IActionResult> GetMessagingWorkspace([FromQuery] string? since)
        {
            var resolved = await ResolvePatientAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            var patient = resolved.patient!;
            var user = resolved.user!;

            DateTime? sinceUtc = null;
            if (!string.IsNullOrWhiteSpace(since)
                && DateTime.TryParse(since, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            {
                sinceUtc = parsed;
            }

            var conversations = await _context.Conversations.AsNoTracking()
                .Include(c => c.Therapist)
                .ThenInclude(t => t.Center)
                .Where(c => c.PatientId == patient.PatientId)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            var conversationIds = conversations.Select(c => c.ConversationId).ToList();

            var allMessages = conversationIds.Count == 0
                ? new List<Message>()
                : await _context.Messages.AsNoTracking()
                    .Where(m => conversationIds.Contains(m.ConversationId))
                    .OrderBy(m => m.SentAt)
                    .ToListAsync();

            //  العدّاد لا يتأثر بـ since: نحتاج كل الرسائل لنعرف غير المقروء فعلًا
            var visibleMessages = sinceUtc.HasValue
                ? allMessages.Where(m => m.SentAt > sinceUtc.Value).ToList()
                : allMessages;

            var senderIds = allMessages.Select(m => m.SenderUserId).Distinct().ToList();
            var senderEmails = await _context.Users.AsNoTracking()
                .Where(u => senderIds.Contains(u.UserId))
                .ToDictionaryAsync(u => u.UserId, u => u.Email);
            var senderTherapistNames = await _context.Therapists.AsNoTracking()
                .Where(t => senderIds.Contains(t.UserId))
                .ToDictionaryAsync(t => t.UserId, t => t.FullName);

            string SenderName(int senderId)
            {
                if (senderId == patient.UserId)
                {
                    return patient.FullName;
                }

                if (senderTherapistNames.TryGetValue(senderId, out var therapistName))
                {
                    return therapistName;
                }

                return senderEmails.TryGetValue(senderId, out var email) ? email : "Unknown";
            }

            object MessageDto(Message m) => new
            {
                id = m.MessageId.ToString(),
                conversationId = m.ConversationId.ToString(),
                senderId = m.SenderUserId.ToString(),
                senderName = SenderName(m.SenderUserId),
                text = m.MessageText,
                attachment = m.AttachmentUrl,
                isRead = m.IsRead,
                sentAt = m.SentAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
            };

            //  المعالجون الذين يقابلهم المريض: عبر محادثة أو موعد سابق
            var appointmentTherapistIds = await _context.Appointments.AsNoTracking()
                .Where(a => a.PatientId == patient.PatientId)
                .Select(a => a.TherapistId)
                .Distinct()
                .ToListAsync();

            var therapistIds = conversations.Select(c => c.TherapistId)
                .Concat(appointmentTherapistIds)
                .Distinct()
                .ToList();

            var therapists = await _context.Therapists.AsNoTracking()
                .Include(t => t.Specialty)
                .Include(t => t.Center)
                .Where(t => therapistIds.Contains(t.TherapistId))
                .ToListAsync();

            var notifications = await _context.Notifications.AsNoTracking()
                .Where(n => n.UserId == user.UserId)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();

            if (sinceUtc.HasValue)
            {
                notifications = notifications.Where(n => n.CreatedAt > sinceUtc.Value).ToList();
            }

            var preferences = await _context.NotificationPreferences.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == user.UserId);

            return Ok(new
            {
                patientProfile = ProfileDto(patient, user),
                notificationPreferences = PreferencesDto(preferences),
                patients = new[]
                {
                    new
                    {
                        id = patient.PatientId.ToString(),
                        name = patient.FullName,
                        imageUrl = patient.ProfileImageUrl,
                        gender = patient.Gender,
                        dateOfBirth = patient.DateOfBirth?.ToString("yyyy-MM-dd")
                    }
                },
                therapists = therapists.Select(t => new
                {
                    id = t.TherapistId.ToString(),
                    name = t.FullName,
                    imageUrl = t.ProfileImageUrl,
                    specialty = t.Specialty == null
                        ? null
                        : CenterMapper.Pick(t.Specialty.NameEnglish, t.Specialty.NameArabic),
                    centerId = t.CenterId.ToString(),
                    centerName = t.Center == null
                        ? null
                        : CenterMapper.Pick(t.Center.NameEnglish, t.Center.NameArabic),
                    experience = t.YearsOfExperience ?? 0,
                    about = t.Bio,
                    availability = t.IsAvailable ? "available" : "unavailable"
                }).ToList(),
                conversations = conversations.Select(c =>
                {
                    var conversationMessages = allMessages
                        .Where(m => m.ConversationId == c.ConversationId)
                        .ToList();
                    var last = conversationMessages.LastOrDefault();

                    return new
                    {
                        id = c.ConversationId.ToString(),
                        patientId = c.PatientId.ToString(),
                        therapistId = c.TherapistId.ToString(),
                        therapistName = c.Therapist?.FullName,
                        therapistImage = c.Therapist?.ProfileImageUrl,
                        centerId = c.Therapist?.CenterId.ToString(),
                        centerName = c.Therapist?.Center == null
                            ? null
                            : CenterMapper.Pick(c.Therapist.Center.NameEnglish,
                                c.Therapist.Center.NameArabic),
                        lastMessage = last?.MessageText,
                        lastMessageAt = last?.SentAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                        unreadCount = conversationMessages.Count(m =>
                            m.SenderUserId != patient.UserId && !m.IsRead),
                        messages = conversationMessages
                            .Where(m => !sinceUtc.HasValue || m.SentAt > sinceUtc.Value)
                            .Select(m => MessageDto(m))
                            .ToList()
                    };
                }).ToList(),
                notifications = notifications.Select(n => new
                {
                    id = n.NotificationId.ToString(),
                    type = n.Type,
                    title = n.Title,
                    message = n.Message,
                    isRead = n.IsRead,
                    date = n.CreatedAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                    relatedId = n.RelatedEntityId?.ToString()
                }).ToList()
            });
        }

        [HttpGet("account")]
        public async Task<IActionResult> GetAccount()
        {
            var resolved = await ResolvePatientAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            return Ok(new
            {
                profile = ProfileDto(resolved.patient!, resolved.user!),
                preferences = PreferencesDto(await _context.NotificationPreferences.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.UserId == resolved.user!.UserId))
            });
        }

        [HttpPut("account/profile")]
        public async Task<IActionResult> UpdateProfile(UpdateProfileDto dto)
        {
            var resolved = await ResolvePatientAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            var patient = resolved.patient!;
            var user = resolved.user!;

            patient.FullName = dto.FullName.Trim();
            patient.Gender = dto.Gender == null ? null : dto.Gender.ToLowerInvariant();
            patient.DateOfBirth = dto.DateOfBirth;
            patient.ProfileImageUrl = dto.ProfileImageUrl;

            if (dto.PhoneNumber != null)
            {
                user.PhoneNumber = dto.PhoneNumber.Trim();
            }

            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { profile = ProfileDto(patient, user) });
        }

        [HttpPut("account/preferences")]
        public async Task<IActionResult> UpdatePreferences(UpdatePreferencesDto dto)
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

            return Ok(new { preferences = PreferencesDto(preferences) });
        }

        //  رفع صورة الحساب أو صورة مرفق: multipart بحقل اسمه file
        //  نسمح بـ ٣ ميجابايت في الطلب حتى نستطيع ردًا واضحًا file_too_large
        //  ثم نطبّق حدّ ٢ ميجابايت الحقيقي على الملف نفسه داخل ImageStorage
        [HttpPost("account/image")]
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

        //  نتأكد أن الـ Token يخص مريضًا موجودًا فعلًا
        private async Task<(Patient? patient, User? user, IActionResult? error)> ResolvePatientAsync()
        {
            if (!CurrentUser.TryGetUserId(User, out var userId))
            {
                return (null, null, ApiError.Unauthorized(
                    "unauthenticated", "Authentication is required."));
            }

            var user = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == userId);

            var patient = await _context.Patients.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId);

            if (user == null || patient == null)
            {
                return (null, null, ApiError.NotFound("not_found", "Patient profile not found."));
            }

            return (patient, user, null);
        }

        private static object ProfileDto(Patient patient, User user) => new
        {
            id = patient.PatientId.ToString(),
            name = patient.FullName,
            email = user.Email,
            phone = user.PhoneNumber,
            gender = patient.Gender,
            dateOfBirth = patient.DateOfBirth?.ToString("yyyy-MM-dd"),
            imageUrl = patient.ProfileImageUrl,
            status = user.IsActive ? "active" : "inactive"
        };

        private static object PreferencesDto(NotificationPreference? preferences) => new
        {
            emailEnabled = preferences?.EmailEnabled ?? true,
            inAppEnabled = preferences?.InAppEnabled ?? true,
            appointmentReminders = preferences?.AppointmentReminders ?? true,
            marketing = preferences?.Marketing ?? false,
            language = preferences?.Language ?? "en"
        };
    }
}