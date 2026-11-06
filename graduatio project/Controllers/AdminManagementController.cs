using graduatio_project.Data;
using graduatio_project.DTOs.Admin;
using graduatio_project.Models;
using graduatio_project.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace graduatio_project.Controllers
{
    /// <summary>
    /// All admin-only management endpoints.
    /// Route prefix: /api/admin
    /// Every action requires [Authorize(Roles = "Admin")].
    /// List responses follow the contract: { items, total, filters }.
    /// Pagination: ?page=1&amp;pageSize=20 (1-based).
    /// Ids are returned as strings; statuses are lowercase.
    /// </summary>
    [Route("api/admin")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminManagementController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly PasswordService _pwd;
        private readonly NotificationService _notifications;

        public AdminManagementController(
            ApplicationDbContext db,
            PasswordService pwd,
            NotificationService notifications)
        {
            _db = db;
            _pwd = pwd;
            _notifications = notifications;
        }

        // ─────────────────────────────────────────────────────────────
        //  HELPERS
        // ─────────────────────────────────────────────────────────────

        private int CurrentUserId()
            => CurrentUser.TryGetUserId(User, out var id) ? id : 0;

        private static string Iso(DateTime dt) => dt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        private static string? Iso(DateTime? dt) => dt?.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

        //  شكل الإشعار الموحّد للوحة ولجرس شريط التنقل
        private static object NotificationDto(Notification n) => new
        {
            id = n.NotificationId.ToString(),
            type = n.Type,
            title = n.Title,
            message = n.Message,
            isRead = n.IsRead,
            relatedId = n.RelatedEntityId?.ToString(),
            createdAt = Iso(n.CreatedAt)
        };

        //  الأعلام الأربعة العامة نفسها التي تستخدمها بقية الأدوار
        private static object PreferencesFlags(NotificationPreference? preferences) => new
        {
            emailEnabled = preferences?.EmailEnabled ?? true,
            inAppEnabled = preferences?.InAppEnabled ?? true,
            appointmentReminders = preferences?.AppointmentReminders ?? true,
            marketing = preferences?.Marketing ?? false,
            language = preferences?.Language ?? "en"
        };

        // ─────────────────────────────────────────────────────────────
        //  1. DASHBOARD   GET /api/admin/dashboard?year=
        // ─────────────────────────────────────────────────────────────

        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard([FromQuery] int? year)
        {
            var now = DateTime.UtcNow;
            int y = year ?? now.Year;
            var yearStart = new DateTime(y, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var yearEnd = yearStart.AddYears(1);
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var lastMonthStart = monthStart.AddMonths(-1);

            //  الأرقام الرئيسية: عدّ حقيقي من الجداول وليس قيمًا ثابتة
            var activeCenters = await _db.Centers.CountAsync(c => c.Status == "Approved");
            var totalPatients = await _db.Patients.CountAsync();
            var totalTherapists = await _db.Therapists.CountAsync();

            //  منحنى كل مؤشر خلال السنة المختارة (يغذي الرسم المصغّر داخل الكرت)
            var centerSeries = await MonthlySeriesAsync(
                _db.Centers.Where(c => c.Status == "Approved" && c.ApprovedAt != null)
                    .Select(c => c.ApprovedAt!.Value),
                yearStart,
                yearEnd);

            var patientSeries = await MonthlySeriesAsync(
                _db.Patients.Select(p => p.User.CreatedAt),
                yearStart,
                yearEnd);

            var therapistSeries = await MonthlySeriesAsync(
                _db.Therapists.Select(t => t.CreatedAt),
                yearStart,
                yearEnd);

            //  حجوزات كل شهر في السنة المختارة
            var monthlyBookings = await MonthlySeriesAsync(
                _db.Appointments.Select(a => a.CreatedAt),
                yearStart,
                yearEnd);

            //  نسبة التغيّر = إضافات هذا الشهر مقابل الشهر السابق
            var centersThisMonth = await _db.Centers.CountAsync(c =>
                c.Status == "Approved" && c.ApprovedAt >= monthStart);
            var centersLastMonth = await _db.Centers.CountAsync(c =>
                c.Status == "Approved" && c.ApprovedAt >= lastMonthStart && c.ApprovedAt < monthStart);

            var patientsThisMonth = await _db.Patients.CountAsync(p => p.User.CreatedAt >= monthStart);
            var patientsLastMonth = await _db.Patients.CountAsync(p =>
                p.User.CreatedAt >= lastMonthStart && p.User.CreatedAt < monthStart);

            var therapistsThisMonth = await _db.Therapists.CountAsync(t => t.CreatedAt >= monthStart);
            var therapistsLastMonth = await _db.Therapists.CountAsync(t =>
                t.CreatedAt >= lastMonthStart && t.CreatedAt < monthStart);

            //  صحة المنصة = نسبة المواعيد المكتملة من المواعيد المحسومة
            var completedAppointments = await _db.Appointments.CountAsync(a => a.Status == "Completed");
            var resolvedAppointments = await _db.Appointments.CountAsync(a =>
                a.Status == "Completed" || a.Status == "Cancelled" || a.Status == "Rejected");
            var healthPercent = resolvedAppointments == 0
                ? 0
                : (int)Math.Round(completedAppointments * 100d / resolvedAppointments);

            //  متوسط زمن مراجعة طلبات المراكز بالساعات
            //  نتجاهل أي صف تاريخ مراجعته أقدم من تاريخ تقديمه (بيانات غير متسقة)
            var reviewMinutes = await _db.Centers
                .Where(c => c.ReviewedAt != null && c.ReviewedAt >= c.CreatedAt)
                .Select(c => (double?)EF.Functions.DateDiffMinute(c.CreatedAt, c.ReviewedAt!.Value))
                .AverageAsync();
            double? approvalResponse = reviewMinutes is double minutes
                ? Math.Round(minutes / 60d, 1)
                : null;

            //  رضا المرضى = متوسط التقييمات
            double? satisfaction = await _db.Reviews.AnyAsync()
                ? Math.Round(await _db.Reviews.AverageAsync(r => (double)r.Rating), 1)
                : null;

            //  ما ينتظر قرارًا فعليًا: مراكز معلّقة، حسابات معالجين موقوفة، تقييمات مخفية
            var pendingCenters = await _db.Centers.CountAsync(c => c.Status == "Pending");
            var inactiveTherapists = await _db.Therapists.CountAsync(t => !t.IsActive);
            var hiddenReviews = await _db.Reviews.CountAsync(r => !r.IsVisible);

            //  آخر الأحداث الحقيقية من الجداول لشريط النشاط
            var events = new List<(string Kind, int Id, DateTime At)>();

            var submissions = await _db.Centers.AsNoTracking()
                .OrderByDescending(c => c.CreatedAt)
                .Take(5)
                .Select(c => new { Id = c.CenterId, At = c.CreatedAt })
                .ToListAsync();
            events.AddRange(submissions.Select(x => ("centerSubmitted", x.Id, x.At)));

            var reviewedCenters = await _db.Centers.AsNoTracking()
                .Where(c => c.ReviewedAt != null)
                .OrderByDescending(c => c.ReviewedAt)
                .Take(5)
                .Select(c => new { Id = c.CenterId, At = c.ReviewedAt!.Value })
                .ToListAsync();
            events.AddRange(reviewedCenters.Select(x => ("centerReviewed", x.Id, x.At)));

            var therapistJoins = await _db.Therapists.AsNoTracking()
                .OrderByDescending(t => t.CreatedAt)
                .Take(5)
                .Select(t => new { Id = t.TherapistId, At = t.CreatedAt })
                .ToListAsync();
            events.AddRange(therapistJoins.Select(x => ("therapistJoined", x.Id, x.At)));

            var reviewPosts = await _db.Reviews.AsNoTracking()
                .OrderByDescending(r => r.CreatedAt)
                .Take(5)
                .Select(r => new { Id = r.ReviewId, At = r.CreatedAt })
                .ToListAsync();
            events.AddRange(reviewPosts.Select(x => ("reviewPosted", x.Id, x.At)));

            var patientJoins = await _db.Patients.AsNoTracking()
                .OrderByDescending(p => p.User.CreatedAt)
                .Take(5)
                .Select(p => new { Id = p.PatientId, At = p.User.CreatedAt })
                .ToListAsync();
            events.AddRange(patientJoins.Select(x => ("patientJoined", x.Id, x.At)));

            var activityText = new Dictionary<string, (string En, string Ar)>
            {
                ["centerSubmitted"] = ("Center application submitted", "تم إرسال طلب مركز"),
                ["centerReviewed"] = ("Center application reviewed", "تمت مراجعة طلب مركز"),
                ["therapistJoined"] = ("Physiotherapist account created", "تم إنشاء حساب معالج"),
                ["reviewPosted"] = ("New patient review", "تم استلام تقييم جديد"),
                ["patientJoined"] = ("Patient registered", "تم تسجيل مريض جديد"),
            };

            var activity = events
                .OrderByDescending(e => e.At)
                .Take(8)
                .Select(e => new
                {
                    id = $"{e.Kind}-{e.Id}",
                    text = new { en = activityText[e.Kind].En, ar = activityText[e.Kind].Ar },
                    at = Iso(e.At)
                })
                .ToList();

            //  إشعارات الأدمن لعرضها في لوحة الإشعارات بالواجهة
            var notificationRows = await _db.Notifications.AsNoTracking()
                .Where(n => n.UserId == CurrentUserId())
                .OrderByDescending(n => n.CreatedAt)
                .Take(25)
                .ToListAsync();
            var notifications = notificationRows.Select(NotificationDto).ToList();

            return Ok(new
            {
                year = y,
                metrics = new
                {
                    activeCenters = new
                    {
                        value = activeCenters,
                        change = ChangePercent(centersThisMonth, centersLastMonth),
                        series = centerSeries
                    },
                    registeredPatients = new
                    {
                        value = totalPatients,
                        change = ChangePercent(patientsThisMonth, patientsLastMonth),
                        series = patientSeries
                    },
                    physiotherapists = new
                    {
                        value = totalTherapists,
                        change = ChangePercent(therapistsThisMonth, therapistsLastMonth),
                        series = therapistSeries
                    }
                },
                monthlyBookings,
                health = new
                {
                    percent = healthPercent,
                    approvalResponse,
                    satisfaction
                },
                pendingApprovals = new
                {
                    total = pendingCenters + inactiveTherapists + hiddenReviews,
                    centers = pendingCenters,
                    therapists = inactiveTherapists,
                    reportedReviews = hiddenReviews
                },
                activity,
                notifications
            });
        }

        //  عدد السجلات لكل شهر في السنة المختارة (12 قيمة، والشهر الفارغ = 0)
        private static async Task<List<int>> MonthlySeriesAsync(
            IQueryable<DateTime> dates,
            DateTime yearStart,
            DateTime yearEnd)
        {
            var rows = await dates
                .Where(d => d >= yearStart && d < yearEnd)
                .GroupBy(d => d.Month)
                .Select(g => new { Month = g.Key, Count = g.Count() })
                .ToListAsync();

            return Enumerable.Range(1, 12)
                .Select(month => rows.FirstOrDefault(r => r.Month == month)?.Count ?? 0)
                .ToList();
        }

        //  نسبة التغيّر بين شهرين؛ لا تُعرض عندما لا يوجد أساس للمقارنة
        private static double? ChangePercent(int current, int previous)
            => previous > 0 ? Math.Round((current - previous) * 100d / previous, 1) : null;

        // ─────────────────────────────────────────────────────────────
        //  NOTIFICATIONS   GET /api/admin/notifications
        //  جرس شريط التنقل: إشعارات الأدمن مع تفضيلات إظهارها
        // ─────────────────────────────────────────────────────────────

        [HttpGet("notifications")]
        public async Task<IActionResult> GetNotifications()
        {
            var rows = await _db.Notifications.AsNoTracking()
                .Where(n => n.UserId == CurrentUserId())
                .OrderByDescending(n => n.CreatedAt)
                .Take(50)
                .ToListAsync();

            var preferences = await _db.NotificationPreferences.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == CurrentUserId());

            return Ok(new
            {
                notifications = rows.Select(NotificationDto).ToList(),
                preferences = PreferencesFlags(preferences)
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  2. CENTERS LIST   GET /api/admin/centers
        // ─────────────────────────────────────────────────────────────

        [HttpGet("centers")]
        public async Task<IActionResult> GetCenters(
            [FromQuery] string? status,
            [FromQuery] string? search,
            [FromQuery] string? city,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var q = _db.Centers.AsNoTracking().Include(c => c.User).AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
                q = q.Where(c => c.Status.ToLower() == status.ToLower());

            if (!string.IsNullOrWhiteSpace(city))
                q = q.Where(c => c.City.ToLower() == city.ToLower());

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.ToLower();
                q = q.Where(c =>
                    c.NameArabic.ToLower().Contains(s) ||
                    (c.NameEnglish != null && c.NameEnglish.ToLower().Contains(s)) ||
                    c.User.Email.ToLower().Contains(s));
            }

            var total = await q.CountAsync();
            var cities = await _db.Centers.AsNoTracking()
                .Select(c => c.City).Distinct().OrderBy(c => c).ToListAsync();

            var items = await q
                .OrderByDescending(c => c.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new
                {
                    id = c.CenterId.ToString(),
                    name = c.NameEnglish ?? c.NameArabic,
                    nameAr = c.NameArabic,
                    nameEn = c.NameEnglish,
                    email = c.User.Email,
                    phone = c.User.PhoneNumber,
                    city = c.City,
                    address = c.Address,
                    status = c.Status.ToLowerInvariant(),
                    profileImageUrl = c.ProfileImageUrl,
                    submittedAt = Iso(c.CreatedAt),
                    approvedAt = Iso(c.ApprovedAt),
                    rejectionReason = c.RejectionReason
                })
                .ToListAsync();

            return Ok(new
            {
                items,
                total,
                filters = new { cities }
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  3. CENTER DETAIL   GET /api/admin/centers/{id}
        // ─────────────────────────────────────────────────────────────

        [HttpGet("centers/{id:int}")]
        public async Task<IActionResult> GetCenter(int id)
        {
            var center = await _db.Centers
                .AsNoTracking()
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.CenterId == id);

            if (center == null)
                return ApiError.NotFound("not_found", "Center not found.");

            var therapists = await _db.Therapists.AsNoTracking()
                .Include(t => t.Specialty)
                .Where(t => t.CenterId == id)
                .Select(t => new
                {
                    id = t.TherapistId.ToString(),
                    name = t.FullName,
                    specialty = t.Specialty.NameEnglish ?? t.Specialty.NameArabic,
                    isActive = t.IsActive,
                    profileImageUrl = t.ProfileImageUrl
                })
                .ToListAsync();

            var services = await _db.Services.AsNoTracking()
                .Include(s => s.Specialty)
                .Where(s => s.CenterId == id)
                .Select(s => new
                {
                    id = s.ServiceId.ToString(),
                    name = s.NameEnglish ?? s.NameArabic,
                    price = s.Price,
                    duration = s.DurationMinutes,
                    isActive = s.IsActive
                })
                .ToListAsync();

            var appointmentStats = await _db.Appointments
                .Where(a => a.CenterId == id)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    total = g.Count(),
                    completed = g.Count(a => a.Status == "Completed"),
                    cancelled = g.Count(a => a.Status == "Cancelled")
                })
                .FirstOrDefaultAsync();

            var reviewStats = await _db.Reviews
                .Where(r => r.CenterId == id)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    total = g.Count(),
                    average = g.Average(r => (double)r.Rating)
                })
                .FirstOrDefaultAsync();

            //  تخصصات المركز مشتقة من تخصصات أخصائييه
            var specialties = await _db.Therapists.AsNoTracking()
                .Where(t => t.CenterId == id)
                .Select(t => t.Specialty.NameEnglish ?? t.Specialty.NameArabic)
                .Distinct()
                .ToListAsync();

            //  الملفات المرفوعة من المركز: قائمة المستندات وصور العيادة
            var documents = await _db.CenterDocuments.AsNoTracking()
                .Where(d => d.CenterId == id)
                .OrderByDescending(d => d.UploadedAt)
                .Select(d => new
                {
                    id = d.CenterDocumentId.ToString(),
                    type = d.DocumentType,
                    url = d.FileUrl,
                    uploadedAt = Iso(d.UploadedAt)
                })
                .ToListAsync();

            var photos = await _db.CenterImages.AsNoTracking()
                .Where(i => i.CenterId == id)
                .OrderByDescending(i => i.IsPrimary)
                .ThenBy(i => i.DisplayOrder)
                .Select(i => new
                {
                    id = i.CenterImageId.ToString(),
                    url = i.ImageUrl,
                    isPrimary = i.IsPrimary
                })
                .ToListAsync();

            return Ok(new
            {
                id = center.CenterId.ToString(),
                name = center.NameEnglish ?? center.NameArabic,
                nameAr = center.NameArabic,
                nameEn = center.NameEnglish,
                descriptionAr = center.DescriptionArabic,
                descriptionEn = center.DescriptionEnglish,
                email = center.User.Email,
                phone = center.User.PhoneNumber,
                city = center.City,
                address = center.Address,
                profileImageUrl = center.ProfileImageUrl,
                websiteUrl = center.WebsiteUrl,
                status = center.Status.ToLowerInvariant(),
                rejectionReason = center.RejectionReason,
                submittedAt = Iso(center.CreatedAt),
                approvedAt = Iso(center.ApprovedAt),
                reviewedAt = Iso(center.ReviewedAt),
                location = new
                {
                    latitude = center.Latitude,
                    longitude = center.Longitude,
                    confirmed = center.LocationConfirmed
                },
                therapists,
                services,
                specialties,
                documents,
                photos,
                stats = new
                {
                    appointments = appointmentStats,
                    reviews = reviewStats == null ? null : new
                    {
                        reviewStats.total,
                        average = reviewStats.total > 0
                            ? Math.Round(reviewStats.average, 1)
                            : (double?)null
                    }
                }
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  4. UPDATE CENTER STATUS   PUT /api/admin/centers/{id}/status
        // ─────────────────────────────────────────────────────────────

        [HttpPut("centers/{id:int}/status")]
        public async Task<IActionResult> UpdateCenterStatus(int id, UpdateStatusDto dto)
        {
            var center = await _db.Centers.FirstOrDefaultAsync(c => c.CenterId == id);
            if (center == null)
                return ApiError.NotFound("not_found", "Center not found.");

            var s = dto.Status.Trim().ToLowerInvariant();
            if (s is not ("approved" or "rejected" or "suspended" or "pending"))
                return ApiError.BadRequest("invalid_status",
                    "Status must be approved, rejected, suspended, or pending.");

            if (s == "rejected" && string.IsNullOrWhiteSpace(dto.Reason))
                return ApiError.Validation(
                    new Dictionary<string, string[]> { ["Reason"] = ["A reason is required when rejecting."] },
                    "A reason is required when rejecting.");

            center.Status = char.ToUpper(s[0]) + s[1..];  // Capitalize for DB
            center.UpdatedAt = DateTime.UtcNow;
            center.RejectionReason = s == "rejected" ? dto.Reason?.Trim() : null;

            if (s == "approved")
            {
                center.ApprovedAt = DateTime.UtcNow;
                center.ReviewedAt = DateTime.UtcNow;
                var admin = await _db.Admins.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.UserId == CurrentUserId());
                center.ApprovedByAdminId = admin?.AdminId;
            }
            else if (s == "rejected")
            {
                center.ReviewedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();

            //  الجرس لصاحب المركز: قرار الاعتماد أو الرفض أو التعليق
            await _notifications.NotifyAsync(
                center.UserId,
                "center",
                "Center status update",
                $"Your center is now {center.Status}.",
                center.CenterId);

            return Ok(new
            {
                id = center.CenterId.ToString(),
                status = center.Status.ToLowerInvariant(),
                rejectionReason = center.RejectionReason,
                updatedAt = Iso(center.UpdatedAt!.Value)
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  5. PATIENTS LIST   GET /api/admin/patients
        // ─────────────────────────────────────────────────────────────

        [HttpGet("patients")]
        public async Task<IActionResult> GetPatients(
            [FromQuery] string? search,
            [FromQuery] string? gender,
            [FromQuery] bool? isActive,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var q = _db.Patients.AsNoTracking().Include(p => p.User).AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.ToLower();
                q = q.Where(p =>
                    p.FullName.ToLower().Contains(s) ||
                    p.User.Email.ToLower().Contains(s) ||
                    p.User.PhoneNumber.Contains(s));
            }

            if (!string.IsNullOrWhiteSpace(gender))
                q = q.Where(p => p.Gender != null && p.Gender.ToLower() == gender.ToLower());

            if (isActive.HasValue)
                q = q.Where(p => p.User.IsActive == isActive.Value);

            var total = await q.CountAsync();

            var items = await q
                .OrderByDescending(p => p.User.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new
                {
                    id = p.PatientId.ToString(),
                    userId = p.UserId.ToString(),
                    fullName = p.FullName,
                    email = p.User.Email,
                    phone = p.User.PhoneNumber,
                    gender = p.Gender,
                    dateOfBirth = p.DateOfBirth.HasValue ? p.DateOfBirth.Value.ToString("yyyy-MM-dd") : null,
                    city = p.City,
                    profileImageUrl = p.ProfileImageUrl,
                    isActive = p.User.IsActive,
                    joinedAt = Iso(p.User.CreatedAt)
                })
                .ToListAsync();

            var genders = await _db.Patients.AsNoTracking()
                .Where(p => p.Gender != null)
                .Select(p => p.Gender!).Distinct().OrderBy(g => g).ToListAsync();

            return Ok(new
            {
                items,
                total,
                filters = new { genders }
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  6. UPDATE PATIENT STATUS   PUT /api/admin/patients/{id}/status
        // ─────────────────────────────────────────────────────────────

        [HttpPut("patients/{id:int}/status")]
        public async Task<IActionResult> UpdatePatientStatus(int id, UpdateStatusDto dto)
        {
            var patient = await _db.Patients
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.PatientId == id);

            if (patient == null)
                return ApiError.NotFound("not_found", "Patient not found.");

            var s = dto.Status.Trim().ToLowerInvariant();
            if (s is not ("active" or "suspended"))
                return ApiError.BadRequest("invalid_status", "Status must be active or suspended.");

            patient.User.IsActive = s == "active";
            patient.User.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                id = patient.PatientId.ToString(),
                isActive = patient.User.IsActive,
                status = patient.User.IsActive ? "active" : "suspended"
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  7. PHYSIOTHERAPISTS LIST   GET /api/admin/physiotherapists
        // ─────────────────────────────────────────────────────────────

        [HttpGet("physiotherapists")]
        public async Task<IActionResult> GetTherapists(
            [FromQuery] string? search,
            [FromQuery] int? centerId,
            [FromQuery] int? specialtyId,
            [FromQuery] bool? isActive,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var q = _db.Therapists.AsNoTracking()
                .Include(t => t.User)
                .Include(t => t.Center)
                .Include(t => t.Specialty)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.ToLower();
                q = q.Where(t =>
                    t.FullName.ToLower().Contains(s) ||
                    t.User.Email.ToLower().Contains(s));
            }

            if (centerId.HasValue)
                q = q.Where(t => t.CenterId == centerId.Value);

            if (specialtyId.HasValue)
                q = q.Where(t => t.SpecialtyId == specialtyId.Value);

            if (isActive.HasValue)
                q = q.Where(t => t.IsActive == isActive.Value);

            var total = await q.CountAsync();

            var items = await q
                .OrderByDescending(t => t.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new
                {
                    id = t.TherapistId.ToString(),
                    userId = t.UserId.ToString(),
                    fullName = t.FullName,
                    fullNameAr = t.FullNameAr,
                    email = t.User.Email,
                    phone = t.User.PhoneNumber,
                    center = new { id = t.CenterId.ToString(), name = t.Center.NameEnglish ?? t.Center.NameArabic },
                    specialty = new { id = t.SpecialtyId.ToString(), name = t.Specialty.NameEnglish ?? t.Specialty.NameArabic },
                    bio = t.Bio,
                    bioAr = t.BioAr,
                    yearsOfExperience = t.YearsOfExperience,
                    profileImageUrl = t.ProfileImageUrl,
                    isActive = t.IsActive,
                    isAvailable = t.IsAvailable,
                    joinedAt = Iso(t.CreatedAt)
                })
                .ToListAsync();

            var specialties = await _db.Specialties.AsNoTracking()
                .Where(s => s.IsActive)
                .Select(s => new { id = s.SpecialtyId.ToString(), name = s.NameEnglish ?? s.NameArabic })
                .ToListAsync();

            var centersFilter = await _db.Centers.AsNoTracking()
                .Where(c => c.Status == "Approved")
                .Select(c => new { id = c.CenterId.ToString(), name = c.NameEnglish ?? c.NameArabic })
                .ToListAsync();

            return Ok(new
            {
                items,
                total,
                filters = new { specialties, centers = centersFilter }
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  8. CREATE THERAPIST   POST /api/admin/physiotherapists
        // ─────────────────────────────────────────────────────────────

        [HttpPost("physiotherapists")]
        public async Task<IActionResult> CreateTherapist(AdminCreateTherapistDto dto)
        {
            // Verify center exists
            var center = await _db.Centers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.CenterId == dto.CenterId);
            if (center == null)
                return ApiError.NotFound("not_found", "Center not found.");

            // Verify specialty exists
            var specialty = await _db.Specialties.AsNoTracking()
                .FirstOrDefaultAsync(s => s.SpecialtyId == dto.SpecialtyId);
            if (specialty == null)
                return ApiError.NotFound("not_found", "Specialty not found.");

            // Email uniqueness check
            if (await _db.Users.AnyAsync(u => u.Email == dto.Email.Trim().ToLowerInvariant()))
                return ApiError.Conflict("email_taken", "A user with this email already exists.");

            // Create User
            var user = new User
            {
                Email = dto.Email.Trim().ToLowerInvariant(),
                PhoneNumber = dto.PhoneNumber.Trim(),
                Role = "Therapist",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            user.PasswordHash = _pwd.HashPassword(dto.Password);
            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            // Create Therapist
            var therapist = new Therapist
            {
                UserId = user.UserId,
                CenterId = dto.CenterId,
                SpecialtyId = dto.SpecialtyId,
                FullName = dto.FullName.Trim(),
                FullNameAr = dto.FullNameAr?.Trim(),
                Bio = dto.Bio?.Trim(),
                BioAr = dto.BioAr?.Trim(),
                YearsOfExperience = dto.YearsOfExperience,
                Qualifications = dto.Qualifications?.Trim(),
                Languages = dto.Languages?.Trim(),
                IsActive = true,
                IsAvailable = true,
                CreatedAt = DateTime.UtcNow
            };
            _db.Therapists.Add(therapist);
            await _db.SaveChangesAsync();

            return StatusCode(201, new
            {
                id = therapist.TherapistId.ToString(),
                userId = user.UserId.ToString(),
                fullName = therapist.FullName,
                email = user.Email,
                phone = user.PhoneNumber,
                center = new { id = center.CenterId.ToString(), name = center.NameEnglish ?? center.NameArabic },
                specialty = new { id = specialty.SpecialtyId.ToString(), name = specialty.NameEnglish ?? specialty.NameArabic },
                isActive = therapist.IsActive
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  9. UPDATE THERAPIST STATUS   PUT /api/admin/physiotherapists/{id}/status
        // ─────────────────────────────────────────────────────────────

        [HttpPut("physiotherapists/{id:int}/status")]
        public async Task<IActionResult> UpdateTherapistStatus(int id, UpdateStatusDto dto)
        {
            var therapist = await _db.Therapists
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.TherapistId == id);

            if (therapist == null)
                return ApiError.NotFound("not_found", "Physiotherapist not found.");

            var s = dto.Status.Trim().ToLowerInvariant();
            if (s is not ("active" or "suspended"))
                return ApiError.BadRequest("invalid_status", "Status must be active or suspended.");

            therapist.IsActive = s == "active";
            therapist.User.IsActive = s == "active";
            therapist.User.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            //  الجرس للمعالج: تغيّر حالة الحساب (تفعيل/تعليق)
            await _notifications.NotifyAsync(
                therapist.UserId,
                "system",
                "Account status updated",
                $"Your account is now {s}.",
                therapist.TherapistId);

            return Ok(new
            {
                id = therapist.TherapistId.ToString(),
                isActive = therapist.IsActive,
                status = therapist.IsActive ? "active" : "suspended"
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  10. APPOINTMENTS LIST   GET /api/admin/appointments
        // ─────────────────────────────────────────────────────────────

        [HttpGet("appointments")]
        public async Task<IActionResult> GetAppointments(
            [FromQuery] string? status,
            [FromQuery] int? centerId,
            [FromQuery] int? therapistId,
            [FromQuery] int? patientId,
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var q = _db.Appointments.AsNoTracking()
                .Include(a => a.Patient).ThenInclude(p => p.User)
                .Include(a => a.Center)
                .Include(a => a.Therapist)
                .Include(a => a.Service)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
                q = q.Where(a => a.Status.ToLower() == status.ToLower());

            if (centerId.HasValue)
                q = q.Where(a => a.CenterId == centerId.Value);

            if (therapistId.HasValue)
                q = q.Where(a => a.TherapistId == therapistId.Value);

            if (patientId.HasValue)
                q = q.Where(a => a.PatientId == patientId.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.ToLower();
                q = q.Where(a =>
                    a.Patient.FullName.ToLower().Contains(s) ||
                    a.Therapist.FullName.ToLower().Contains(s) ||
                    (a.Center.NameEnglish != null && a.Center.NameEnglish.ToLower().Contains(s)) ||
                    a.Center.NameArabic.ToLower().Contains(s));
            }

            var total = await q.CountAsync();

            var items = await q
                .OrderByDescending(a => a.StartDateTime)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new
                {
                    id = a.AppointmentId.ToString(),
                    patient = new { id = a.PatientId.ToString(), name = a.Patient.FullName },
                    center = new { id = a.CenterId.ToString(), name = a.Center.NameEnglish ?? a.Center.NameArabic },
                    therapist = new { id = a.TherapistId.ToString(), name = a.Therapist.FullName },
                    service = new { id = a.ServiceId.ToString(), name = a.Service.NameEnglish ?? a.Service.NameArabic },
                    startDateTime = Iso(a.StartDateTime),
                    endDateTime = Iso(a.EndDateTime),
                    price = a.PriceAtBooking,
                    status = a.Status.ToLowerInvariant(),
                    patientNotes = a.PatientNotes,
                    rejectionReason = a.RejectionReason,
                    cancellationReason = a.CancellationReason,
                    createdAt = Iso(a.CreatedAt)
                })
                .ToListAsync();

            var statuses = new[] { "pending", "confirmed", "completed", "cancelled", "rejected" };

            return Ok(new
            {
                items,
                total,
                filters = new { statuses }
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  11. APPOINTMENT OPTIONS   GET /api/admin/appointments/options
        //      Returns all centres/therapists/patients/services for form dropdowns
        // ─────────────────────────────────────────────────────────────

        [HttpGet("appointments/options")]
        public async Task<IActionResult> GetAppointmentOptions()
        {
            var centers = await _db.Centers.AsNoTracking()
                .Where(c => c.Status == "Approved")
                .Select(c => new { id = c.CenterId.ToString(), name = c.NameEnglish ?? c.NameArabic })
                .OrderBy(c => c.name)
                .ToListAsync();

            var therapists = await _db.Therapists.AsNoTracking()
                .Include(t => t.Specialty)
                .Where(t => t.IsActive)
                .Select(t => new
                {
                    id = t.TherapistId.ToString(),
                    name = t.FullName,
                    centerId = t.CenterId.ToString(),
                    specialty = t.Specialty.NameEnglish ?? t.Specialty.NameArabic
                })
                .OrderBy(t => t.name)
                .ToListAsync();

            var patients = await _db.Patients.AsNoTracking()
                .Include(p => p.User)
                .Where(p => p.User.IsActive)
                .Select(p => new
                {
                    id = p.PatientId.ToString(),
                    name = p.FullName,
                    email = p.User.Email
                })
                .OrderBy(p => p.name)
                .ToListAsync();

            var services = await _db.Services.AsNoTracking()
                .Where(s => s.IsActive)
                .Select(s => new
                {
                    id = s.ServiceId.ToString(),
                    name = s.NameEnglish ?? s.NameArabic,
                    centerId = s.CenterId.ToString(),
                    price = s.Price,
                    duration = s.DurationMinutes
                })
                .OrderBy(s => s.name)
                .ToListAsync();

            return Ok(new { centers, therapists, patients, services });
        }

        // ─────────────────────────────────────────────────────────────
        //  12. CREATE APPOINTMENT   POST /api/admin/appointments
        // ─────────────────────────────────────────────────────────────

        [HttpPost("appointments")]
        public async Task<IActionResult> CreateAppointment(AdminCreateAppointmentDto dto)
        {
            var patient = await _db.Patients.AsNoTracking()
                .FirstOrDefaultAsync(p => p.PatientId == dto.PatientId);
            if (patient == null)
                return ApiError.NotFound("not_found", "Patient not found.");

            var center = await _db.Centers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.CenterId == dto.CenterId);
            if (center == null)
                return ApiError.NotFound("not_found", "Center not found.");

            var therapist = await _db.Therapists.AsNoTracking()
                .FirstOrDefaultAsync(t => t.TherapistId == dto.TherapistId);
            if (therapist == null)
                return ApiError.NotFound("not_found", "Physiotherapist not found.");

            var service = await _db.Services.AsNoTracking()
                .FirstOrDefaultAsync(s => s.ServiceId == dto.ServiceId);
            if (service == null)
                return ApiError.NotFound("not_found", "Service not found.");

            if (dto.EndDateTime <= dto.StartDateTime)
                return ApiError.BadRequest("invalid_time", "EndDateTime must be after StartDateTime.");

            var appointment = new Appointment
            {
                PatientId = dto.PatientId,
                CenterId = dto.CenterId,
                TherapistId = dto.TherapistId,
                ServiceId = dto.ServiceId,
                StartDateTime = dto.StartDateTime,
                EndDateTime = dto.EndDateTime,
                PriceAtBooking = service.Price,
                PatientNotes = dto.PatientNotes?.Trim(),
                Status = "Confirmed",  // Admin-created appointments are auto-confirmed
                CreatedAt = DateTime.UtcNow
            };

            _db.Appointments.Add(appointment);
            await _db.SaveChangesAsync();

            return StatusCode(201, new
            {
                id = appointment.AppointmentId.ToString(),
                patientId = appointment.PatientId.ToString(),
                centerId = appointment.CenterId.ToString(),
                therapistId = appointment.TherapistId.ToString(),
                serviceId = appointment.ServiceId.ToString(),
                startDateTime = Iso(appointment.StartDateTime),
                endDateTime = Iso(appointment.EndDateTime),
                price = appointment.PriceAtBooking,
                status = appointment.Status.ToLowerInvariant(),
                createdAt = Iso(appointment.CreatedAt)
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  13. REVIEWS LIST   GET /api/admin/reviews
        // ─────────────────────────────────────────────────────────────

        [HttpGet("reviews")]
        public async Task<IActionResult> GetReviews(
            [FromQuery] string? visibility,   // all | visible | hidden
            [FromQuery] int? centerId,
            [FromQuery] int? minRating,
            [FromQuery] int? maxRating,
            [FromQuery] string? search,
            [FromQuery] string? sort,       // newest (default) | oldest | highest
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var q = _db.Reviews.AsNoTracking()
                .Include(r => r.Patient)
                .Include(r => r.Center)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(visibility) && visibility.ToLower() != "all")
                q = q.Where(r => r.IsVisible == (visibility.ToLower() == "visible"));

            if (centerId.HasValue)
                q = q.Where(r => r.CenterId == centerId.Value);

            if (minRating.HasValue)
                q = q.Where(r => r.Rating >= minRating.Value);

            if (maxRating.HasValue)
                q = q.Where(r => r.Rating <= maxRating.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                //  البحث يغطي نص التقييم والمريض والمركز والمعالج (كما يَعِد به حقل البحث في الواجهة)
                var s = search.ToLower();
                q = q.Where(r =>
                    (r.Comment != null && r.Comment.ToLower().Contains(s)) ||
                    r.Patient.FullName.ToLower().Contains(s) ||
                    (r.Center.NameEnglish != null && r.Center.NameEnglish.ToLower().Contains(s)) ||
                    (r.Center.NameArabic != null && r.Center.NameArabic.ToLower().Contains(s)) ||
                    r.Appointment.Therapist.FullName.ToLower().Contains(s));
            }

            var total = await q.CountAsync();

            // Summary counts
            var allReviews = await _db.Reviews.AsNoTracking().ToListAsync();
            var summary = new
            {
                total = allReviews.Count,
                visible = allReviews.Count(r => r.IsVisible),
                hidden = allReviews.Count(r => !r.IsVisible),
                replied = allReviews.Count(r => r.Reply != null),
                averageRating = allReviews.Count > 0
                    ? Math.Round(allReviews.Average(r => (double)r.Rating), 1)
                    : (double?)null,
                ratingDistribution = Enumerable.Range(1, 5)
                    .Select(i => new { rating = i, count = allReviews.Count(r => r.Rating == i) })
                    .ToList()
            };

            var ordered = sort?.ToLowerInvariant() switch
            {
                "oldest" => q.OrderBy(r => r.CreatedAt),
                "highest" => q.OrderByDescending(r => r.Rating)
                    .ThenByDescending(r => r.CreatedAt),
                _ => q.OrderByDescending(r => r.CreatedAt)
            };

            var items = await ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(r => new
                {
                    id = r.ReviewId.ToString(),
                    patient = new { id = r.PatientId.ToString(), name = r.Patient.FullName, imageUrl = r.Patient.ProfileImageUrl },
                    center = new { id = r.CenterId.ToString(), name = r.Center.NameEnglish ?? r.Center.NameArabic },
                    therapistName = r.Appointment.Therapist.FullName,
                    appointmentId = r.AppointmentId.ToString(),
                    rating = r.Rating,
                    comment = r.Comment,
                    reply = r.Reply,
                    repliedAt = Iso(r.RepliedAt),
                    isVisible = r.IsVisible,
                    createdAt = Iso(r.CreatedAt)
                })
                .ToListAsync();

            return Ok(new { items, total, summary, filters = new { } });
        }

        // ─────────────────────────────────────────────────────────────
        //  14. MODERATE REVIEW   POST /api/admin/reviews/{id}/moderate
        // ─────────────────────────────────────────────────────────────

        [HttpPost("reviews/{id:int}/moderate")]
        public async Task<IActionResult> ModerateReview(int id, ModerateReviewDto dto)
        {
            var review = await _db.Reviews.FirstOrDefaultAsync(r => r.ReviewId == id);
            if (review == null)
                return ApiError.NotFound("not_found", "Review not found.");

            var action = dto.Action.Trim().ToLowerInvariant();

            switch (action)
            {
                case "hide":
                    review.IsVisible = false;
                    break;
                case "show":
                    review.IsVisible = true;
                    break;
                case "remove":
                    _db.Reviews.Remove(review);
                    await _db.SaveChangesAsync();
                    return Ok(new { message = "Review removed successfully." });
                default:
                    return ApiError.BadRequest("invalid_action", "Action must be hide, show, or remove.");
            }

            review.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                id = review.ReviewId.ToString(),
                isVisible = review.IsVisible,
                action
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  15. GET SETTINGS   GET /api/admin/settings
        //  Settings are stored on the Admin profile row; no separate table needed.
        // ─────────────────────────────────────────────────────────────

        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings()
        {
            var uid = CurrentUserId();
            var admin = await _db.Admins.AsNoTracking()
                .Include(a => a.User)
                .FirstOrDefaultAsync(a => a.UserId == uid);

            if (admin == null)
                return ApiError.NotFound("not_found", "Admin profile not found.");

            var preferences = await _db.NotificationPreferences.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == uid);

            return Ok(new
            {
                adminId = admin.AdminId.ToString(),
                adminFullName = admin.FullName,
                email = admin.User.Email,
                phone = admin.User.PhoneNumber,
                // Site-wide toggles can be extended with a dedicated settings table later
                maintenanceMode = false,
                //  تفضيلات الإشعارات: نفس الأعلام الأربعة المستخدمة في كل الأدوار
                notifications = PreferencesFlags(preferences)
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  16. SAVE SETTINGS   PUT /api/admin/settings
        // ─────────────────────────────────────────────────────────────

        [HttpPut("settings")]
        public async Task<IActionResult> SaveSettings(AdminSettingsDto dto)
        {
            var uid = CurrentUserId();
            var admin = await _db.Admins
                .Include(a => a.User)
                .FirstOrDefaultAsync(a => a.UserId == uid);

            if (admin == null)
                return ApiError.NotFound("not_found", "Admin profile not found.");

            if (!string.IsNullOrWhiteSpace(dto.AdminFullName))
                admin.FullName = dto.AdminFullName.Trim();

            if (!string.IsNullOrWhiteSpace(dto.ContactPhone))
            {
                admin.User.PhoneNumber = dto.ContactPhone.Trim();
                admin.User.UpdatedAt = DateTime.UtcNow;
            }

            // Email change not supported here — use /api/auth/change-email if needed
            // MaintenanceMode would need a global settings table; ignored for now

            //  حفظ تفضيلات الإشعارات في صف المستخدم الخاص بالأدمن
            if (dto.Notifications != null)
            {
                var preferences = await _db.NotificationPreferences
                    .FirstOrDefaultAsync(p => p.UserId == uid);

                if (preferences == null)
                {
                    preferences = new NotificationPreference { UserId = uid };
                    _db.NotificationPreferences.Add(preferences);
                }

                if (dto.Notifications.EmailEnabled.HasValue)
                    preferences.EmailEnabled = dto.Notifications.EmailEnabled.Value;
                if (dto.Notifications.InAppEnabled.HasValue)
                    preferences.InAppEnabled = dto.Notifications.InAppEnabled.Value;
                if (dto.Notifications.AppointmentReminders.HasValue)
                    preferences.AppointmentReminders = dto.Notifications.AppointmentReminders.Value;
                if (dto.Notifications.Marketing.HasValue)
                    preferences.Marketing = dto.Notifications.Marketing.Value;
            }

            await _db.SaveChangesAsync();

            return Ok(new
            {
                adminId = admin.AdminId.ToString(),
                adminFullName = admin.FullName,
                email = admin.User.Email,
                phone = admin.User.PhoneNumber,
                maintenanceMode = false
            });
        }
    }
}
