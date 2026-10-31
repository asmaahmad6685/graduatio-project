using System.Globalization;
using graduatio_project.Data;
using graduatio_project.DTOs.Center;
using graduatio_project.Models;
using graduatio_project.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace graduatio_project.Controllers
{
    //  مساحة المركز: كل ما يحتاجه فريق المركز في شاشة واحدة
    //  الروابط:
    //    GET  /api/center/workspace
    //    PUT  /api/center/workspace/center
    //    PUT  /api/center/workspace/hours
    //    POST /api/center/workspace/images            DELETE /api/center/workspace/images/{imageId}
    //    POST /api/center/workspace/services          PUT/DELETE /api/center/workspace/services/{serviceId}
    //    PUT/DELETE /api/center/workspace/therapists/{therapistId}
    //    GET/POST /api/center/workspace/patients      GET/POST /api/center/workspace/patients/{patientId}/notes
    //    PUT  /api/center/workspace/appointments/{appointmentId}/status
    //    POST /api/center/workspace/reviews/{reviewId}/reply
    //    GET/PUT /api/center/workspace/preferences
    [Route("api/center")]
    [ApiController]
    [Authorize(Roles = "Center")]
    public class CenterWorkspaceController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly PasswordService _passwordService;

        public CenterWorkspaceController(
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            PasswordService passwordService)
        {
            _context = context;
            _environment = environment;
            _passwordService = passwordService;
        }

        [HttpGet("workspace")]
        public async Task<IActionResult> GetWorkspace()
        {
            var resolved = await ResolveCenterAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            var center = resolved.center!;
            var user = resolved.user!;

            var mapped = await CenterMapper.BuildAsync(_context, new[] { center });
            var centerDto = mapped[center.CenterId];

            var therapists = await _context.Therapists.AsNoTracking()
                .Include(t => t.Specialty)
                .Include(t => t.User)
                .Where(t => t.CenterId == center.CenterId)
                .ToListAsync();

            var services = await _context.Services.AsNoTracking()
                .Include(s => s.Specialty)
                .Where(s => s.CenterId == center.CenterId)
                .OrderByDescending(s => s.IsActive)
                .ToListAsync();

            var appointmentRows = await _context.Appointments.AsNoTracking()
                .Where(a => a.CenterId == center.CenterId)
                .ToListAsync();

            //  المرضى يظهرون عبر موعد أو ملاحظة أو لأن هذا المركز أضافهم مباشرة
            var patientIds = appointmentRows.Select(a => a.PatientId)
                .Distinct()
                .Concat(await _context.PatientNotes.AsNoTracking()
                    .Where(n => n.CenterId == center.CenterId)
                    .Select(n => n.PatientId)
                    .Distinct()
                    .ToListAsync())
                .Concat(await _context.CenterPatients.AsNoTracking()
                    .Where(cp => cp.CenterId == center.CenterId)
                    .Select(cp => cp.PatientId)
                    .ToListAsync())
                .Distinct()
                .ToList();

            var patients = await _context.Patients.AsNoTracking()
                .Include(p => p.User)
                .Where(p => patientIds.Contains(p.PatientId))
                .ToListAsync();

            var appointmentCounts = appointmentRows
                .GroupBy(a => a.PatientId)
                .ToDictionary(g => g.Key, g => g.Count());

            //  قائمة التخصصات النشطة: نماذج الإضافة تحتاجها لاختيار تخصص الخدمة
            //  أو المعالج بدل تخمينه من سجل موجود
            var specialties = await _context.Specialties.AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.SpecialtyId)
                .Select(s => new
                {
                    id = s.SpecialtyId.ToString(),
                    nameEnglish = s.NameEnglish,
                    nameArabic = s.NameArabic
                })
                .ToListAsync();

            var therapistIds = therapists.Select(t => t.TherapistId).ToList();
            var therapistNameById = therapists.ToDictionary(t => t.TherapistId, t => t.FullName);
            var therapistUserIds = therapists.Select(t => t.UserId).ToHashSet();

            //  خدمات كل معالج محمّلة دفعة واحدة حتى لا نستعلم داخل عرض القائمة
            var therapistServiceIds = therapistIds.Count == 0
                ? new Dictionary<int, List<string>>()
                : await _context.TherapistServices.AsNoTracking()
                    .Where(x => therapistIds.Contains(x.TherapistId))
                    .GroupBy(x => x.TherapistId)
                    .ToDictionaryAsync(g => g.Key,
                        g => g.Select(x => x.ServiceId.ToString()).ToList());

            var loaded = await AppointmentMapper.LoadAsync(_context,
                _context.Appointments.Where(a => a.CenterId == center.CenterId));

            var reviews = await _context.Reviews.AsNoTracking()
                .Include(r => r.Patient)
                .Include(r => r.Appointment)
                .Where(r => r.CenterId == center.CenterId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            var notifications = await _context.Notifications.AsNoTracking()
                .Where(n => n.UserId == user.UserId)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();

            var conversationTherapistIds = therapistIds;
            var conversations = conversationTherapistIds.Count == 0
                ? new List<Conversation>()
                : await _context.Conversations.AsNoTracking()
                    .Include(c => c.Patient)
                    .Where(c => conversationTherapistIds.Contains(c.TherapistId))
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

            return Ok(new
            {
                center = new
                {
                    centerDto,
                    //  الحقول المزدوجة تُعاد منفصلة حتى تستطيع شاشة التعديل
                    //  إعادتها كما هي دون أن تضيع النسخة العربية أو الإنجليزية
                    nameArabic = center.NameArabic,
                    descriptionArabic = center.DescriptionArabic,
                    email = user.Email,
                    phone = user.PhoneNumber,
                    approvalStatus = center.Status.ToLowerInvariant(),
                    reviewedAt = center.ReviewedAt?.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                    reviewReason = center.RejectionReason
                },
                therapists = therapists.Select(t => new
                {
                    id = t.TherapistId.ToString(),
                    name = t.FullName,
                    imageUrl = t.ProfileImageUrl,
                    bio = t.Bio,
                    email = t.User?.Email,
                    phone = t.User?.PhoneNumber,
                    specialtyId = t.SpecialtyId,
                    specialty = t.Specialty == null
                        ? null
                        : CenterMapper.Pick(t.Specialty.NameEnglish, t.Specialty.NameArabic),
                    experience = t.YearsOfExperience ?? 0,
                    credentials = t.Qualifications,
                    languages = t.Languages,
                    availability = t.IsAvailable && t.IsActive ? "available" : "unavailable",
                    isActive = t.IsActive,
                    serviceIds = therapistServiceIds.TryGetValue(t.TherapistId, out var ids)
                        ? ids
                        : new List<string>()
                }).ToList(),
                services = services.Select(s => new
                {
                    id = s.ServiceId.ToString(),
                    name = CenterMapper.Pick(s.NameEnglish, s.NameArabic),
                    nameEnglish = s.NameEnglish,
                    nameArabic = s.NameArabic,
                    description = CenterMapper.Pick(s.DescriptionEnglish, s.DescriptionArabic),
                    descriptionEnglish = s.DescriptionEnglish,
                    descriptionArabic = s.DescriptionArabic,
                    price = s.Price,
                    duration = s.DurationMinutes,
                    specialtyId = s.SpecialtyId,
                    specialty = s.Specialty == null
                        ? null
                        : CenterMapper.Pick(s.Specialty.NameEnglish, s.Specialty.NameArabic),
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
                    status = p.User != null && p.User.IsActive ? "active" : "inactive",
                    appointmentCount = appointmentCounts.TryGetValue(p.PatientId, out var count)
                        ? count
                        : 0
                }).ToList(),
                specialties,
                appointments = AppointmentMapper.Build(
                    loaded.appointments, loaded.centers, loaded.therapists, loaded.services),
                reviews = reviews.Select(r => new
                {
                    id = r.ReviewId.ToString(),
                    patientId = r.PatientId.ToString(),
                    patientName = r.Patient?.FullName,
                    appointmentId = r.AppointmentId.ToString(),
                    rating = r.Rating,
                    text = r.Comment,
                    date = r.CreatedAt.ToString("yyyy-MM-dd"),
                    isVisible = r.IsVisible,
                    reply = r.Reply,
                    repliedAt = r.RepliedAt?.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
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
                }).ToList(),
                conversations = conversations.Select(c => new
                {
                    id = c.ConversationId.ToString(),
                    patientId = c.PatientId.ToString(),
                    patientName = c.Patient?.FullName,
                    patientImage = c.Patient?.ProfileImageUrl,
                    therapistId = c.TherapistId.ToString(),
                    therapistName = therapistNameById.TryGetValue(c.TherapistId, out var convTherapist)
                        ? convTherapist
                        : null,
                    lastMessage = conversationMessages
                        .LastOrDefault(m => m.ConversationId == c.ConversationId)?.MessageText,
                    unreadCount = conversationMessages.Count(m =>
                        m.ConversationId == c.ConversationId && !m.IsRead
                        && !therapistUserIds.Contains(m.SenderUserId))
                }).ToList(),
                today = new
                {
                    date = todayLocal.ToString("yyyy-MM-dd"),
                    appointmentCount = todayRows.Count,
                    appointments = AppointmentMapper.Build(
                        todayRows, loaded.centers, loaded.therapists, loaded.services)
                }
            });
        }

        [HttpPut("workspace/center")]
        public async Task<IActionResult> UpdateCenter(UpdateCenterInfoDto dto)
        {
            var resolved = await ResolveCenterAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            var center = resolved.center!;

            if (dto.NameEnglish != null) center.NameEnglish = dto.NameEnglish.Trim();
            if (dto.NameArabic != null) center.NameArabic = dto.NameArabic.Trim();
            if (dto.DescriptionEnglish != null) center.DescriptionEnglish = dto.DescriptionEnglish.Trim();
            if (dto.DescriptionArabic != null) center.DescriptionArabic = dto.DescriptionArabic.Trim();
            if (dto.City != null) center.City = dto.City.Trim();
            if (dto.Address != null) center.Address = dto.Address.Trim();
            if (dto.ProfileImageUrl != null) center.ProfileImageUrl = dto.ProfileImageUrl.Trim();
            if (dto.WebsiteUrl != null) center.WebsiteUrl = dto.WebsiteUrl.Trim();
            if (dto.Latitude.HasValue) center.Latitude = dto.Latitude;
            if (dto.Longitude.HasValue) center.Longitude = dto.Longitude;
            if (dto.LocationConfirmed.HasValue) center.LocationConfirmed = dto.LocationConfirmed.Value;

            center.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var user = await _context.Users.AsNoTracking()
                .FirstAsync(u => u.UserId == center.UserId);

            var mapped = await CenterMapper.BuildAsync(_context, new[] { center });
            return Ok(new
            {
                centerDto = mapped[center.CenterId],
                nameArabic = center.NameArabic,
                descriptionArabic = center.DescriptionArabic,
                email = user.Email,
                phone = user.PhoneNumber
            });
        }

        [HttpPut("workspace/hours")]
        public async Task<IActionResult> UpdateHours(UpdateWorkingHoursDto dto)
        {
            var resolved = await ResolveCenterAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            var centerId = resolved.center!.CenterId;

            var errors = new Dictionary<string, string[]>();
            if (!TimeOnly.TryParseExact(dto.WeekStart, "HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _)) errors                    ["WeekStart"] = new[] { "Use HH:MM." };
            if (!TimeOnly.TryParseExact(dto.WeekEnd, "HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _)) errors                    ["WeekEnd"] = new[] { "Use HH:MM." };
            if (!TimeOnly.TryParseExact(dto.FridayStart, "HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _)) errors                    ["FridayStart"] = new[] { "Use HH:MM." };
            if (!TimeOnly.TryParseExact(dto.FridayEnd, "HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _)) errors                    ["FridayEnd"] = new[] { "Use HH:MM." };

            if (errors.Count > 0)
            {
                return ApiError.Validation(errors);
            }

            var weekStart = TimeOnly.Parse(dto.WeekStart, CultureInfo.InvariantCulture);
            var weekEnd = TimeOnly.Parse(dto.WeekEnd, CultureInfo.InvariantCulture);
            var fridayStart = TimeOnly.Parse(dto.FridayStart, CultureInfo.InvariantCulture);
            var fridayEnd = TimeOnly.Parse(dto.FridayEnd, CultureInfo.InvariantCulture);

            var existing = await _context.CenterWorkingHours
                .Where(w => w.CenterId == centerId)
                .ToListAsync();

            foreach (var hour in existing)
            {
                if (hour.DayOfWeek == DayOfWeek.Friday)
                {
                    hour.OpeningTime = fridayStart;
                    hour.ClosingTime = fridayEnd;
                }
                else
                {
                    hour.OpeningTime = weekStart;
                    hour.ClosingTime = weekEnd;
                }

                hour.IsClosed = false;
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                weekStart = weekStart.ToString("HH:mm"),
                weekEnd = weekEnd.ToString("HH:mm"),
                fridayStart = fridayStart.ToString("HH:mm"),
                fridayEnd = fridayEnd.ToString("HH:mm")
            });
        }

        [HttpPost("workspace/images")]
        [RequestSizeLimit(ImageStorage.MaxBytes + 1024 * 1024)]
        public async Task<IActionResult> AddImage([FromForm] AddCenterImageDto dto, IFormFile? file)
        {
            var resolved = await ResolveCenterAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            string url;
            if (file != null && file.Length > 0)
            {
                var validation = ImageStorage.Validate(file);
                if (validation != null)
                {
                    return validation;
                }

                url = await ImageStorage.SaveAsync(file, _environment);
            }
            else if (!string.IsNullOrWhiteSpace(dto.ImageUrl))
            {
                url = dto.ImageUrl.Trim();
            }
            else
            {
                return ApiError.BadRequest("invalid_upload", "No file was uploaded.",
                    new Dictionary<string, string[]>
                    {
                        ["file"] = new[] { "Upload a file or provide an image URL." }
                    });
            }

            var centerId = resolved.center!.CenterId;

            if (dto.IsPrimary)
            {
                var current = await _context.CenterImages
                    .Where(i => i.CenterId == centerId && i.IsPrimary)
                    .ToListAsync();
                foreach (var image in current)
                {
                    image.IsPrimary = false;
                }
            }

            var maxOrder = await _context.CenterImages.AsNoTracking()
                .Where(i => i.CenterId == centerId)
                .MaxAsync(i => (int?)i.DisplayOrder) ?? 0;

            var imageRow = new CenterImage
            {
                CenterId = centerId,
                ImageUrl = url,
                IsPrimary = dto.IsPrimary,
                DisplayOrder = maxOrder + 1
            };

            _context.CenterImages.Add(imageRow);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                id = imageRow.CenterImageId.ToString(),
                url = imageRow.ImageUrl,
                isPrimary = imageRow.IsPrimary
            });
        }

        [HttpDelete("workspace/images/{imageId:int}")]
        public async Task<IActionResult> DeleteImage(int imageId)
        {
            var resolved = await ResolveCenterAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            var image = await _context.CenterImages
                .FirstOrDefaultAsync(i => i.CenterImageId == imageId
                    && i.CenterId == resolved.center!.CenterId);

            if (image == null)
            {
                return ApiError.NotFound("not_found", "Image not found.");
            }

            _context.CenterImages.Remove(image);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpPost("workspace/services")]
        public async Task<IActionResult> AddService(SaveServiceDto dto)
        {
            var resolved = await ResolveCenterAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            var specialtyError = await ValidateSpecialtyAsync(dto.SpecialtyId);
            if (specialtyError != null)
            {
                return specialtyError;
            }

            var service = new Service
            {
                CenterId = resolved.center!.CenterId,
                SpecialtyId = dto.SpecialtyId,
                NameEnglish = dto.NameEnglish.Trim(),
                NameArabic = dto.NameArabic.Trim(),
                DescriptionEnglish = dto.DescriptionEnglish,
                DescriptionArabic = dto.DescriptionArabic,
                Price = dto.Price,
                DurationMinutes = dto.DurationMinutes,
                IsActive = dto.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            _context.Services.Add(service);
            await _context.SaveChangesAsync();

            return StatusCode(StatusCodes.Status201Created, new
            {
                id = service.ServiceId.ToString(),
                name = service.NameEnglish,
                price = service.Price,
                duration = service.DurationMinutes,
                isActive = service.IsActive
            });
        }

        [HttpPut("workspace/services/{serviceId:int}")]
        public async Task<IActionResult> UpdateService(int serviceId, SaveServiceDto dto)
        {
            var resolved = await ResolveCenterAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            var service = await _context.Services
                .FirstOrDefaultAsync(s => s.ServiceId == serviceId
                    && s.CenterId == resolved.center!.CenterId);

            if (service == null)
            {
                return ApiError.NotFound("not_found", "Service not found.");
            }

            var specialtyError = await ValidateSpecialtyAsync(dto.SpecialtyId);
            if (specialtyError != null)
            {
                return specialtyError;
            }

            service.SpecialtyId = dto.SpecialtyId;
            service.NameEnglish = dto.NameEnglish.Trim();
            service.NameArabic = dto.NameArabic.Trim();
            service.DescriptionEnglish = dto.DescriptionEnglish;
            service.DescriptionArabic = dto.DescriptionArabic;
            service.Price = dto.Price;
            service.DurationMinutes = dto.DurationMinutes;
            service.IsActive = dto.IsActive;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                id = service.ServiceId.ToString(),
                name = service.NameEnglish,
                price = service.Price,
                duration = service.DurationMinutes,
                isActive = service.IsActive
            });
        }

        //  الإلغاء هنا تعطيل: الخدمات قد تكون مرتبطة بمواعيد موجودة
        //  فلا نحذف صفًا يحمل تاريخ حجز، ونُخفيه فقط عن الواجهات العامة
        [HttpDelete("workspace/services/{serviceId:int}")]
        public async Task<IActionResult> DeactivateService(int serviceId)
        {
            var resolved = await ResolveCenterAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            var service = await _context.Services
                .FirstOrDefaultAsync(s => s.ServiceId == serviceId
                    && s.CenterId == resolved.center!.CenterId);

            if (service == null)
            {
                return ApiError.NotFound("not_found", "Service not found.");
            }

            service.IsActive = false;
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpPut("workspace/therapists/{therapistId:int}")]
        public async Task<IActionResult> UpdateTherapist(int therapistId, UpdateTherapistDto dto)
        {
            var resolved = await ResolveCenterAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            var therapist = await _context.Therapists
                .FirstOrDefaultAsync(t => t.TherapistId == therapistId
                    && t.CenterId == resolved.center!.CenterId);

            if (therapist == null)
            {
                return ApiError.NotFound("not_found", "Therapist not found.");
            }

            var specialtyError = await ValidateSpecialtyAsync(dto.SpecialtyId);
            if (specialtyError != null)
            {
                return specialtyError;
            }

            therapist.FullName = dto.FullName.Trim();
            therapist.SpecialtyId = dto.SpecialtyId;
            //  النموذج لا يحرر هذه الحقول، فالقيمة null تعني "أبقِ ما هو محفوظ"
            if (dto.Bio != null) therapist.Bio = dto.Bio;
            if (dto.Qualifications != null) therapist.Qualifications = dto.Qualifications;
            if (dto.Languages != null) therapist.Languages = dto.Languages;
            therapist.ProfileImageUrl = dto.ProfileImageUrl;
            therapist.YearsOfExperience = dto.YearsOfExperience;
            therapist.IsAvailable = dto.IsAvailable;
            therapist.IsActive = dto.IsActive;

            //  ربط المعالج بالخدمات التي يقدمها فعليًا
            if (dto.ServiceIds != null)
            {
                var requested = dto.ServiceIds.Distinct().ToList();
                var ownedByCenter = await _context.Services.AsNoTracking()
                    .Where(s => s.CenterId == therapist.CenterId
                        && requested.Contains(s.ServiceId))
                    .Select(s => s.ServiceId)
                    .ToListAsync();

                if (ownedByCenter.Count != requested.Count)
                {
                    return ApiError.Validation(new Dictionary<string, string[]>
                    {
                        ["ServiceIds"] = new[] { "One or more services do not belong to this center." }
                    }, "One or more services do not belong to this center.");
                }

                var existing = await _context.TherapistServices
                    .Where(x => x.TherapistId == therapistId)
                    .ToListAsync();

                _context.TherapistServices.RemoveRange(
                    existing.Where(x => !ownedByCenter.Contains(x.ServiceId)));

                foreach (var serviceId in ownedByCenter)
                {
                    if (existing.All(x => x.ServiceId != serviceId))
                    {
                        _context.TherapistServices.Add(new TherapistService
                        {
                            TherapistId = therapistId,
                            ServiceId = serviceId
                        });
                    }
                }
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                id = therapist.TherapistId.ToString(),
                name = therapist.FullName,
                specialtyId = therapist.SpecialtyId,
                bio = therapist.Bio,
                imageUrl = therapist.ProfileImageUrl,
                experience = therapist.YearsOfExperience ?? 0,
                credentials = therapist.Qualifications,
                languages = therapist.Languages,
                availability = therapist.IsAvailable && therapist.IsActive
                    ? "available"
                    : "unavailable",
                isActive = therapist.IsActive
            });
        }

        [HttpDelete("workspace/therapists/{therapistId:int}")]
        public async Task<IActionResult> DeactivateTherapist(int therapistId)
        {
            var resolved = await ResolveCenterAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            var therapist = await _context.Therapists
                .FirstOrDefaultAsync(t => t.TherapistId == therapistId
                    && t.CenterId == resolved.center!.CenterId);

            if (therapist == null)
            {
                return ApiError.NotFound("not_found", "Therapist not found.");
            }

            //  المعالج قد يكون له مواعيد وحساب دخول: نعطّله بدل حذفه
            therapist.IsActive = false;
            therapist.IsAvailable = false;
            await _context.SaveChangesAsync();

            return NoContent();
        }

        //  إضافة مريض من مساحة المركز
        //  المريض يحتاج حساب دخول، لذلك ننشئ User و Patient ونربطهما بالمركز
        //  حتى يظهر المريض في قائمة هذا المركز مباشرة بعد الإضافة
        [HttpPost("workspace/patients")]
        public async Task<IActionResult> AddPatient(CreateCenterPatientDto dto)
        {
            var resolved = await ResolveCenterAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            var email = dto.Email.Trim();

            if (await _context.Users.AnyAsync(u => u.Email == email))
            {
                return ApiError.Conflict("duplicate_email", "Email is already registered.",
                    new Dictionary<string, string[]>
                    {
                        ["Email"] = new[] { "An account with this email already exists." }
                    });
            }

            var gender = NormalizeGender(dto.Gender);
            var centerId = resolved.center!.CenterId;

            await using var transaction = await _context.Database.BeginTransactionAsync();

            var user = new User
            {
                Email = email,
                PhoneNumber = dto.PhoneNumber.Trim(),
                PasswordHash = _passwordService.HashPassword(dto.Password),
                Role = "Patient",
                IsActive = dto.IsActive,
                CreatedAt = DateTime.UtcNow
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var patient = new Patient
            {
                UserId = user.UserId,
                FullName = dto.FullName.Trim(),
                Gender = gender,
                DateOfBirth = dto.DateOfBirth,
                ProfileImageUrl = string.IsNullOrWhiteSpace(dto.ProfileImageUrl)
                    ? null
                    : dto.ProfileImageUrl.Trim(),
                City = string.IsNullOrWhiteSpace(dto.City) ? null : dto.City.Trim()
            };
            _context.Patients.Add(patient);
            await _context.SaveChangesAsync();

            _context.CenterPatients.Add(new CenterPatient
            {
                CenterId = centerId,
                PatientId = patient.PatientId
            });
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            return StatusCode(StatusCodes.Status201Created, new
            {
                id = patient.PatientId.ToString(),
                name = patient.FullName,
                email = user.Email,
                phone = user.PhoneNumber,
                gender = patient.Gender,
                dateOfBirth = patient.DateOfBirth?.ToString("yyyy-MM-dd"),
                imageUrl = patient.ProfileImageUrl,
                status = user.IsActive ? "active" : "inactive",
                appointmentCount = 0
            });
        }

        //  تعديل مريض من مساحة المركز: تحديث الملف ورقم الهاتف والإيميل والحالة
        [HttpPut("workspace/patients/{patientId:int}")]
        public async Task<IActionResult> UpdatePatient(int patientId, UpdateCenterPatientDto dto)
        {
            var resolved = await ResolveCenterAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            var centerId = resolved.center!.CenterId;

            //  المركز يعدل فقط مريضًا يظهر في قائمته
            if (!await CenterHasPatientAsync(centerId, patientId))
            {
                return ApiError.NotFound("not_found", "Patient not found.");
            }

            var patient = await _context.Patients
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.PatientId == patientId);

            if (patient?.User == null)
            {
                return ApiError.NotFound("not_found", "Patient not found.");
            }

            var email = dto.Email.Trim();

            if (await _context.Users.AnyAsync(u => u.Email == email && u.UserId != patient.UserId))
            {
                return ApiError.Conflict("duplicate_email", "Email is already registered.",
                    new Dictionary<string, string[]>
                    {
                        ["Email"] = new[] { "An account with this email already exists." }
                    });
            }

            patient.FullName = dto.FullName.Trim();
            patient.Gender = NormalizeGender(dto.Gender);
            patient.DateOfBirth = dto.DateOfBirth;
            patient.ProfileImageUrl = string.IsNullOrWhiteSpace(dto.ProfileImageUrl)
                ? null
                : dto.ProfileImageUrl.Trim();
            patient.City = string.IsNullOrWhiteSpace(dto.City) ? null : dto.City.Trim();
            patient.User.Email = email;
            patient.User.PhoneNumber = dto.PhoneNumber.Trim();
            patient.User.IsActive = dto.IsActive;
            patient.User.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var appointmentCount = await _context.Appointments.AsNoTracking()
                .CountAsync(a => a.CenterId == centerId && a.PatientId == patientId);

            return Ok(new
            {
                id = patient.PatientId.ToString(),
                name = patient.FullName,
                email = patient.User.Email,
                phone = patient.User.PhoneNumber,
                gender = patient.Gender,
                dateOfBirth = patient.DateOfBirth?.ToString("yyyy-MM-dd"),
                imageUrl = patient.ProfileImageUrl,
                status = patient.User.IsActive ? "active" : "inactive",
                appointmentCount
            });
        }

        [HttpGet("workspace/patients")]
        public async Task<IActionResult> GetPatients()
        {
            var resolved = await ResolveCenterAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            var centerId = resolved.center!.CenterId;

            var appointmentPatientIds = await _context.Appointments.AsNoTracking()
                .Where(a => a.CenterId == centerId)
                .Select(a => a.PatientId)
                .Distinct()
                .ToListAsync();

            var notedPatientIds = await _context.PatientNotes.AsNoTracking()
                .Where(n => n.CenterId == centerId)
                .Select(n => n.PatientId)
                .Distinct()
                .ToListAsync();

            var addedPatientIds = await _context.CenterPatients.AsNoTracking()
                .Where(cp => cp.CenterId == centerId)
                .Select(cp => cp.PatientId)
                .ToListAsync();

            var patientIds = appointmentPatientIds
                .Concat(notedPatientIds)
                .Concat(addedPatientIds)
                .Distinct()
                .ToList();

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
                imageUrl = p.ProfileImageUrl,
                status = p.User != null && p.User.IsActive ? "active" : "inactive"
            }).ToList());
        }

        [HttpGet("workspace/patients/{patientId:int}/notes")]
        public async Task<IActionResult> GetPatientNotes(int patientId)
        {
            var resolved = await ResolveCenterAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            var notes = await _context.PatientNotes.AsNoTracking()
                .Where(n => n.CenterId == resolved.center!.CenterId && n.PatientId == patientId)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();

            return Ok(notes.Select(n => new
            {
                id = n.PatientNoteId.ToString(),
                text = n.Text,
                authorUserId = n.AuthorUserId.ToString(),
                date = n.CreatedAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
            }).ToList());
        }

        [HttpPost("workspace/patients/{patientId:int}/notes")]
        public async Task<IActionResult> AddPatientNote(int patientId, AddPatientNoteDto dto)
        {
            var resolved = await ResolveCenterAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            var patientExists = await _context.Patients.AsNoTracking()
                .AnyAsync(p => p.PatientId == patientId);

            if (!patientExists)
            {
                return ApiError.NotFound("not_found", "Patient not found.");
            }

            var note = new PatientNote
            {
                CenterId = resolved.center!.CenterId,
                PatientId = patientId,
                AuthorUserId = resolved.user!.UserId,
                Text = dto.Text.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            _context.PatientNotes.Add(note);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                id = note.PatientNoteId.ToString(),
                text = note.Text,
                authorUserId = note.AuthorUserId.ToString(),
                date = note.CreatedAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
            });
        }

        [HttpPut("workspace/appointments/{appointmentId:int}/status")]
        public async Task<IActionResult> UpdateAppointmentStatus(
            int appointmentId, UpdateAppointmentStatusDto dto)
        {
            var resolved = await ResolveCenterAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            var appointment = await _context.Appointments
                .FirstOrDefaultAsync(a => a.AppointmentId == appointmentId
                    && a.CenterId == resolved.center!.CenterId);

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

        [HttpPost("workspace/reviews/{reviewId:int}/reply")]
        public async Task<IActionResult> ReplyToReview(int reviewId, ReplyToReviewDto dto)
        {
            var resolved = await ResolveCenterAsync();
            if (resolved.error != null)
            {
                return resolved.error;
            }

            var review = await _context.Reviews
                .FirstOrDefaultAsync(r => r.ReviewId == reviewId
                    && r.CenterId == resolved.center!.CenterId);

            if (review == null)
            {
                return ApiError.NotFound("not_found", "Review not found.");
            }

            review.Reply = dto.Reply.Trim();
            review.RepliedAt = DateTime.UtcNow;
            review.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                id = review.ReviewId.ToString(),
                reply = review.Reply,
                repliedAt = review.RepliedAt?.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
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

        //  المريض ينتمي للمركز عبر رابط مباشر أو موعد أو ملاحظة
        private async Task<bool> CenterHasPatientAsync(int centerId, int patientId)
        {
            if (await _context.CenterPatients.AsNoTracking()
                .AnyAsync(cp => cp.CenterId == centerId && cp.PatientId == patientId))
            {
                return true;
            }

            if (await _context.Appointments.AsNoTracking()
                .AnyAsync(a => a.CenterId == centerId && a.PatientId == patientId))
            {
                return true;
            }

            return await _context.PatientNotes.AsNoTracking()
                .AnyAsync(n => n.CenterId == centerId && n.PatientId == patientId);
        }

        //  الجنس يخزّن male/female فقط، و"غير محدد" يبقى فارغًا
        private static string? NormalizeGender(string? gender)
        {
            var value = gender?.Trim().ToLowerInvariant();
            return value is "male" or "female" ? value : null;
        }

        private async Task<IActionResult?> ValidateSpecialtyAsync(int specialtyId)
        {
            var exists = await _context.Specialties.AsNoTracking()
                .AnyAsync(s => s.SpecialtyId == specialtyId);

            return exists
                ? null
                : ApiError.Validation(new Dictionary<string, string[]>
                {
                    ["SpecialtyId"] = new[] { "Specialty does not exist." }
                }, "Specialty does not exist.");
        }

        private async Task<(Center? center, User? user, IActionResult? error)> ResolveCenterAsync()
        {
            if (!CurrentUser.TryGetUserId(User, out var userId))
            {
                return (null, null, ApiError.Unauthorized(
                    "unauthenticated", "Authentication is required."));
            }

            var user = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == userId);
            var center = await _context.Centers
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (user == null || center == null)
            {
                return (null, null, ApiError.NotFound("not_found", "Center profile not found."));
            }

            return (center, user, null);
        }
    }
}