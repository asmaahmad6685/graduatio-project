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
// using graduatio_project.Data;
// using graduatio_project.DTOs.Patient;
// using graduatio_project.Models;
// using Microsoft.AspNetCore.Authorization;
// using Microsoft.AspNetCore.Mvc;
// using Microsoft.Data.SqlClient;
// using Microsoft.EntityFrameworkCore;
// using System.Data;
// using System.Security.Claims;

// namespace graduatio_project.Controllers
// {
//     [Route("api/[controller]")]
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

            var preferences = await _context.NotificationPreferences.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == user.UserId);

            //  تذكيرات المواعيد: تُنشأ عند التحميل إن كان التذكير مفعّلًا (مثل
            //  وضع العرض التجريبي)، ونحذف أي تذكير لم يعد صالحًا: موعد أُلغي
            //  أو خرج من نافذة الساعات الأربع والعشرين القادمة
            var nowLocal = DateTime.Now;
            var reminders = await _context.Notifications
                .Where(n => n.UserId == user.UserId && n.Type == "appointmentReminder")
                .ToListAsync();

            var reminderAppointmentIds = reminders
                .Where(n => n.RelatedEntityId != null)
                .Select(n => n.RelatedEntityId!.Value)
                .Distinct()
                .ToList();
            var reminderAppointments = await _context.Appointments.AsNoTracking()
                .Where(a => reminderAppointmentIds.Contains(a.AppointmentId))
                .Select(a => new { a.AppointmentId, a.Status, a.StartDateTime })
                .ToListAsync();
            var appointmentById = reminderAppointments.ToDictionary(a => a.AppointmentId);

            var staleReminders = reminders.Where(n =>
                n.RelatedEntityId == null
                || !appointmentById.TryGetValue(n.RelatedEntityId.Value, out var reminderAppointment)
                || reminderAppointment.Status != "Confirmed"
                || reminderAppointment.StartDateTime <= nowLocal
                || reminderAppointment.StartDateTime > nowLocal.AddHours(24)).ToList();

            if (staleReminders.Count > 0)
            {
                _context.Notifications.RemoveRange(staleReminders);
            }

            if (preferences?.AppointmentReminders != false)
            {
                var inWindowIds = await _context.Appointments.AsNoTracking()
                    .Where(a => a.PatientId == patient.PatientId
                        && a.Status == "Confirmed"
                        && a.StartDateTime > nowLocal
                        && a.StartDateTime <= nowLocal.AddHours(24))
                    .Select(a => a.AppointmentId)
                    .ToListAsync();

                var existingIds = reminders
                    .Where(n => !staleReminders.Contains(n) && n.RelatedEntityId != null)
                    .Select(n => n.RelatedEntityId!.Value)
                    .ToHashSet();

                foreach (var appointmentId in inWindowIds.Where(id => !existingIds.Contains(id)))
                {
                    _context.Notifications.Add(new Notification
                    {
                        UserId = user.UserId,
                        Type = "appointmentReminder",
                        Title = "Upcoming appointment",
                        Message = "You have a confirmed appointment within the next 24 hours.",
                        RelatedEntityId = appointmentId
                    });
                }
            }

            await _context.SaveChangesAsync();

            var notifications = await _context.Notifications.AsNoTracking()
                .Where(n => n.UserId == user.UserId)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();

            if (sinceUtc.HasValue)
            {
                notifications = notifications.Where(n => n.CreatedAt > sinceUtc.Value).ToList();
            }

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
                    nameAr = t.FullNameAr,
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
                    bioAr = t.BioAr,
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
            patient.DateOfBirth = dto.DateOfBirth.HasValue
                    ? DateOnly.FromDateTime(dto.DateOfBirth.Value)
                    : null;
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

//         public PatientController(ApplicationDbContext context)
//         {
//             _context = context;
//         }

//         [HttpGet("profile")]
//         [ProducesResponseType(typeof(PatientProfileDto), StatusCodes.Status200OK)]
//         public async Task<IActionResult> GetProfile()
//         {
//             var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
//             if (!int.TryParse(userIdClaim?.Value, out int userId))
//             {
//                 return Unauthorized(new { message = "Invalid token." });
//             }

//             var profile = await _context.Patients
//                 .AsNoTracking()
//                 .Where(p => p.UserId == userId)
//                 .Select(p => new PatientProfileDto
//                 {
//                     PatientId = p.PatientId,
//                     FullName = p.FullName,
//                     Email = p.User.Email,
//                     PhoneNumber = p.User.PhoneNumber,
//                     Gender = p.Gender,
//                     ProfileImageUrl = p.ProfileImageUrl,
//                     DateOfBirth = p.DateOfBirth,
//                     City = p.City
//                 })
//                 .FirstOrDefaultAsync();

//             if (profile == null)
//             {
//                 return NotFound(new { message = "Patient not found." });
//             }

//             return Ok(profile);
//         }

//         [HttpGet("saved-centers")]
//         [ProducesResponseType(typeof(List<SavedCenterDto>), StatusCodes.Status200OK)]
//         public async Task<IActionResult> GetSavedCenters()
//         {
//             var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
//             if (!int.TryParse(userIdClaim?.Value, out int userId))
//             {
//                 return Unauthorized(new { message = "Invalid token." });
//             }

//             var patientId = await GetPatientIdAsync(userId);
//             if (patientId == null)
//             {
//                 return NotFound(new { message = "Patient not found." });
//             }

//             // Keep existing favorites visible even if a center's status changes.
//             var centers = await _context.PatientSavedCenters
//                 .AsNoTracking()
//                 .Where(sc => sc.PatientId == patientId.Value)
//                 .OrderByDescending(sc => sc.SavedAt)
//                 .ThenBy(sc => sc.CenterId)
//                 .Select(sc => new SavedCenterDto
//                 {
//                     CenterId = sc.CenterId,
//                     NameArabic = sc.Center.NameArabic,
//                     NameEnglish = sc.Center.NameEnglish,
//                     DescriptionArabic = sc.Center.DescriptionArabic,
//                     DescriptionEnglish = sc.Center.DescriptionEnglish,
//                     City = sc.Center.City,
//                     Address = sc.Center.Address,
//                     ProfileImageUrl = sc.Center.ProfileImageUrl,
//                     Latitude = sc.Center.Latitude,
//                     Longitude = sc.Center.Longitude,
//                     Status = sc.Center.Status,
//                     IsSaved = true,
//                     SavedAt = sc.SavedAt
//                 })
//                 .ToListAsync();

//             return Ok(centers);
//         }

//         [HttpPut("saved-centers/{centerId:int}")]
//         public async Task<IActionResult> SaveCenter(int centerId)
//         {
//             var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
//             if (!int.TryParse(userIdClaim?.Value, out int userId))
//             {
//                 return Unauthorized(new { message = "Invalid token." });
//             }

//             var patientId = await GetPatientIdAsync(userId);
//             if (patientId == null)
//             {
//                 return NotFound(new { message = "Patient not found." });
//             }

//             // Hold center/account read locks until commit so eligibility cannot
//             // change between validation and insertion, including under RCSI.
//             await using var transaction = await _context.Database
//                 .BeginTransactionAsync(IsolationLevel.Serializable);

//             var center = await _context.Centers
//                 .AsNoTracking()
//                 .Where(c => c.CenterId == centerId)
//                 .Select(c => new { c.Status, c.User.IsActive })
//                 .FirstOrDefaultAsync();

//             if (center == null)
//             {
//                 return NotFound(new { message = "Center not found." });
//             }

//             // Lock the composite key (or its gap if absent). UPDLOCK avoids
//             // competing saves both taking shared locks then attempting inserts.
//             // DELETE must also wait for this lock, preventing the former
//             // duplicate-key/recheck race. Values are SQL parameters.
//             var existingSave = await _context.PatientSavedCenters
//                 .FromSqlInterpolated($@"
//                     SELECT [PatientId], [CenterId], [SavedAt]
//                     FROM [PatientSavedCenters] WITH (UPDLOCK, HOLDLOCK)
//                     WHERE [PatientId] = {patientId.Value} AND [CenterId] = {centerId}")
//                 .AsNoTracking()
//                 .FirstOrDefaultAsync();

//             // A repeated save remains successful even after a status change.
//             if (existingSave != null)
//             {
//                 await transaction.CommitAsync();
//                 return Ok(new { message = "Center saved successfully.", centerId, isSaved = true });
//             }

//             if (center.Status != "Approved" || !center.IsActive)
//             {
//                 return BadRequest(new { message = "Only approved, active centers can be saved." });
//             }

//             var savedCenter = new PatientSavedCenter
//             {
//                 PatientId = patientId.Value,
//                 CenterId = centerId
//             };

//             _context.PatientSavedCenters.Add(savedCenter);

//             await _context.SaveChangesAsync();
//             await transaction.CommitAsync();

//             return Ok(new { message = "Center saved successfully.", centerId, isSaved = true });
//         }

//         [HttpDelete("saved-centers/{centerId:int}")]
//         public async Task<IActionResult> RemoveSavedCenter(int centerId)
//         {
//             var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
//             if (!int.TryParse(userIdClaim?.Value, out int userId))
//             {
//                 return Unauthorized(new { message = "Invalid token." });
//             }

//             var patientId = await GetPatientIdAsync(userId);
//             if (patientId == null)
//             {
//                 return NotFound(new { message = "Patient not found." });
//             }

//             bool centerExists = await _context.Centers.AnyAsync(c => c.CenterId == centerId);
//             if (!centerExists)
//             {
//                 return NotFound(new { message = "Center not found." });
//             }

//             // Deleting zero rows is also success, including concurrent removals.
//             await _context.PatientSavedCenters
//                 .Where(sc => sc.PatientId == patientId.Value && sc.CenterId == centerId)
//                 .ExecuteDeleteAsync();

//             return Ok(new { message = "Center removed from saved centers.", centerId, isSaved = false });
//         }

//         private Task<int?> GetPatientIdAsync(int userId)
//         {
//             return _context.Patients
//                 .Where(p => p.UserId == userId)
//                 .Select(p => (int?)p.PatientId)
//                 .FirstOrDefaultAsync();
//         }

//         [HttpPut("profile")]
//         public async Task<IActionResult> UpdateProfile(UpdatePatientProfileDto dto)
//         {
//             var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
//             if (!int.TryParse(userIdClaim?.Value, out int userId))
//             {
//                 return Unauthorized(new { message = "Invalid token." });
//             }

//             var patient = await _context.Patients
//                 .Include(p => p.User)
//                 .FirstOrDefaultAsync(p => p.UserId == userId);

//             if (patient == null)
//             {
//                 return NotFound(new { message = "Patient not found." });
//             }

//             bool emailExists = await _context.Users
//                 .AnyAsync(u => u.Email == dto.Email && u.UserId != userId);

//             if (emailExists)
//             {
//                 return BadRequest(new { message = "Email is already registered." });
//             }

//             patient.FullName = dto.FullName;
//             patient.User.Email = dto.Email;
//             patient.User.PhoneNumber = dto.PhoneNumber;
//             patient.Gender = dto.Gender;
//             patient.ProfileImageUrl = dto.ProfileImageUrl;
//             patient.DateOfBirth = dto.DateOfBirth;
//             patient.City = dto.City;

//             try
//             {
//                 // Save both linked records together. The unique email index also
//                 // protects against another request claiming this email concurrently.
//                 await _context.SaveChangesAsync();
//             }
//             catch (DbUpdateException ex) when (
//                 ex.InnerException is SqlException sqlException &&
//                 (sqlException.Number == 2601 || sqlException.Number == 2627))
//             {
//                 return BadRequest(new { message = "Email is already registered." });
//             }

//             return Ok(new { message = "Patient profile updated successfully." });
//         }
//     }
// }
