using System.Globalization;
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

        //  العمر بالسنوات في تاريخ معيّن (العمود "العمر" في قائمة المرضى)
        private static int AgeOn(DateOnly birthDate, DateOnly today)
        {
            var age = today.Year - birthDate.Year;
            if (birthDate > today.AddYears(-age)) age--;
            return Math.Max(0, age);
        }

        //  نصوص أحداث سجل النشاط، مشتركة بين لوحة التحكم والقائمة الكاملة
        private static readonly Dictionary<string, (string En, string Ar)> ActivityText = new()
        {
            ["centerSubmitted"] = ("Center application submitted", "تم إرسال طلب مركز"),
            ["centerReviewed"] = ("Center application reviewed", "تمت مراجعة طلب مركز"),
            ["therapistJoined"] = ("Physiotherapist account created", "تم إنشاء حساب معالج"),
            ["reviewPosted"] = ("New patient review", "تم استلام تقييم جديد"),
            ["patientJoined"] = ("Patient registered", "تم تسجيل مريض جديد"),
        };

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

            var activity = events
                .OrderByDescending(e => e.At)
                .Take(8)
                .Select(e => new
                {
                    id = $"{e.Kind}-{e.Id}",
                    kind = e.Kind,
                    relatedId = e.Id.ToString(),
                    text = new { en = ActivityText[e.Kind].En, ar = ActivityText[e.Kind].Ar },
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
        //  1b. ACTIVITY LOG   GET /api/admin/activity?page=&pageSize=
        //  Every real event from the tables, newest first, paginated.
        // ─────────────────────────────────────────────────────────────

        [HttpGet("activity")]
        public async Task<IActionResult> GetActivity(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            //  Each source projects the same shape, so the five of them become
            //  one UNION ALL and paging happens in the database.
            var submissions = _db.Centers.AsNoTracking()
                .Select(c => new { kind = "centerSubmitted", id = c.CenterId, at = c.CreatedAt });

            var reviewedCenters = _db.Centers.AsNoTracking()
                .Where(c => c.ReviewedAt != null)
                .Select(c => new { kind = "centerReviewed", id = c.CenterId, at = c.ReviewedAt!.Value });

            var therapistJoins = _db.Therapists.AsNoTracking()
                .Select(t => new { kind = "therapistJoined", id = t.TherapistId, at = t.CreatedAt });

            var reviewPosts = _db.Reviews.AsNoTracking()
                .Select(r => new { kind = "reviewPosted", id = r.ReviewId, at = r.CreatedAt });

            var patientJoins = _db.Patients.AsNoTracking()
                .Select(p => new { kind = "patientJoined", id = p.PatientId, at = p.User.CreatedAt });

            var events = submissions
                .Concat(reviewedCenters)
                .Concat(therapistJoins)
                .Concat(reviewPosts)
                .Concat(patientJoins);

            var total = await events.CountAsync();
            var rows = await events
                .OrderByDescending(e => e.at)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var items = rows.Select(e => new
            {
                id = $"{e.kind}-{e.id}",
                kind = e.kind,
                relatedId = e.id.ToString(),
                text = new { en = ActivityText[e.kind].En, ar = ActivityText[e.kind].Ar },
                at = Iso(e.at)
            }).ToList();

            return Ok(new { items, total, page, pageSize });
        }

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
                    //  Active therapists of the centre: the list's "Active Therapists"
                    //  column. Without this the client shows 0 for every centre.
                    activeTherapists = _db.Therapists.Count(t =>
                        t.CenterId == c.CenterId && t.IsActive),
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
        //  4b. EDIT CENTER   PUT /api/admin/centers/{id}
        // ─────────────────────────────────────────────────────────────

        [HttpPut("centers/{id:int}")]
        public async Task<IActionResult> UpdateCenter(int id, AdminUpdateCenterDto dto)
        {
            var center = await _db.Centers
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.CenterId == id);

            if (center == null)
                return ApiError.NotFound("not_found", "Center not found.");

            var nameEnglish = dto.NameEnglish?.Trim();
            var nameArabic = dto.NameArabic?.Trim();
            if (string.IsNullOrWhiteSpace(nameEnglish) && string.IsNullOrWhiteSpace(nameArabic))
                return ApiError.Validation(
                    new Dictionary<string, string[]> { ["NameArabic"] = ["A center name is required."] },
                    "A center name is required.");

            if (!string.IsNullOrWhiteSpace(nameArabic))
                center.NameArabic = nameArabic;
            if (nameEnglish != null)
                center.NameEnglish = nameEnglish;
            if (!string.IsNullOrWhiteSpace(dto.City))
                center.City = dto.City.Trim();
            if (dto.Address != null)
                center.Address = dto.Address.Trim();
            if (dto.WebsiteUrl != null)
                center.WebsiteUrl = string.IsNullOrWhiteSpace(dto.WebsiteUrl) ? null : dto.WebsiteUrl.Trim();
            if (dto.DescriptionEnglish != null)
                center.DescriptionEnglish = dto.DescriptionEnglish.Trim();
            if (!string.IsNullOrWhiteSpace(dto.PhoneNumber))
                center.User.PhoneNumber = dto.PhoneNumber.Trim();

            center.UpdatedAt = DateTime.UtcNow;
            center.User.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                id = center.CenterId.ToString(),
                name = center.NameEnglish ?? center.NameArabic,
                city = center.City,
                address = center.Address,
                websiteUrl = center.WebsiteUrl,
                phone = center.User.PhoneNumber,
                status = center.Status.ToLowerInvariant(),
                updatedAt = Iso(center.UpdatedAt!.Value)
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  4c. DELETE CENTER   DELETE /api/admin/centers/{id}
        //  Its own appointments, reviews, services and media go with it; a
        //  center that still owns therapist accounts is refused, because those
        //  are separate logins the admin has to move or remove first.
        // ─────────────────────────────────────────────────────────────

        [HttpDelete("centers/{id:int}")]
        public async Task<IActionResult> DeleteCenter(int id)
        {
            var center = await _db.Centers.AsNoTracking().FirstOrDefaultAsync(c => c.CenterId == id);
            if (center == null)
                return ApiError.NotFound("not_found", "Center not found.");

            var therapistCount = await _db.Therapists.CountAsync(t => t.CenterId == id);
            if (therapistCount > 0)
                return ApiError.Conflict(
                    "center_has_therapists",
                    $"This center still has {therapistCount} therapist(s). Remove or move them before deleting it.",
                    new { therapists = therapistCount });

            var userId = center.UserId;
            var serviceIds = await _db.Services.Where(s => s.CenterId == id)
                .Select(s => s.ServiceId).ToListAsync();
            var appointmentIds = await _db.Appointments.Where(a => a.CenterId == id)
                .Select(a => a.AppointmentId).ToListAsync();

            await using var tx = await _db.Database.BeginTransactionAsync();

            await _db.TherapistServices.Where(ts => serviceIds.Contains(ts.ServiceId)).ExecuteDeleteAsync();
            await _db.Reviews.Where(r => r.CenterId == id || appointmentIds.Contains(r.AppointmentId)).ExecuteDeleteAsync();
            await _db.AppointmentStatusHistories
                .Where(h => appointmentIds.Contains(h.AppointmentId) || h.ChangedByUserId == userId)
                .ExecuteDeleteAsync();
            await _db.Appointments.Where(a => a.CenterId == id).ExecuteDeleteAsync();
            await _db.Services.Where(s => s.CenterId == id).ExecuteDeleteAsync();
            await _db.CenterImages.Where(i => i.CenterId == id).ExecuteDeleteAsync();
            await _db.CenterDocuments.Where(d => d.CenterId == id).ExecuteDeleteAsync();
            await _db.CenterWorkingHours.Where(w => w.CenterId == id).ExecuteDeleteAsync();
            await _db.CenterPatients.Where(cp => cp.CenterId == id).ExecuteDeleteAsync();
            await _db.PatientSavedCenters.Where(s => s.CenterId == id).ExecuteDeleteAsync();
            await _db.PatientNotes.Where(n => n.CenterId == id).ExecuteDeleteAsync();
            await _db.Notifications.Where(n => n.UserId == userId).ExecuteDeleteAsync();
            await _db.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync();
            await _db.ExternalLogins.Where(e => e.UserId == userId).ExecuteDeleteAsync();
            await _db.PasswordResetTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync();
            await _db.Centers.Where(c => c.CenterId == id).ExecuteDeleteAsync();
            await _db.Users.Where(u => u.UserId == userId).ExecuteDeleteAsync();

            await tx.CommitAsync();

            return Ok(new { id = id.ToString(), deleted = true });
        }

        // ─────────────────────────────────────────────────────────────
        //  5. PATIENTS LIST   GET /api/admin/patients
        // ─────────────────────────────────────────────────────────────

        [HttpGet("patients")]
        public async Task<IActionResult> GetPatients(
            [FromQuery] string? search,
            [FromQuery] string? gender,
            [FromQuery] string? city,
            [FromQuery] string? sort,       // recent (آخر نشاط) | newest (default)
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

            //  المدينة تُقارن بدون حساسية لحالة الأحرف لأن الأسماء تصل من
            //  قائمة الاختيار كما خزنتها السجلات
            if (!string.IsNullOrWhiteSpace(city))
            {
                var cityName = city.Trim().ToLower();
                q = q.Where(p => p.City != null && p.City.ToLower() == cityName);
            }

            if (isActive.HasValue)
                q = q.Where(p => p.User.IsActive == isActive.Value);

            var total = await q.CountAsync();

            //  الصفوف أولًا ثم إحصاء المواعيد دفعة واحدة، حتى تصل أعمدة الجدول
            //  كاملة (العمر، عدد المواعيد، آخر زيارة، الحالة)
            //  الترتيب الافتراضي: الأحدث انضمامًا. "recent" يعني الأحدث نشاطًا:
            //  آخر موعد للمريض، وإذا لم يكن له مواعيد فتاريخ انضمامه
            IOrderedQueryable<Patient> ordered = sort?.Trim().ToLowerInvariant() == "recent"
                ? q.OrderByDescending(p => _db.Appointments
                        .Where(a => a.PatientId == p.PatientId)
                        .Max(a => (DateTime?)a.StartDateTime) ?? p.User.CreatedAt)
                    .ThenByDescending(p => p.User.CreatedAt)
                : q.OrderByDescending(p => p.User.CreatedAt);

            var rows = await ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new
                {
                    patientId = p.PatientId,
                    userId = p.UserId,
                    fullName = p.FullName,
                    email = p.User.Email,
                    phone = p.User.PhoneNumber,
                    gender = p.Gender,
                    dateOfBirth = p.DateOfBirth,
                    city = p.City,
                    profileImageUrl = p.ProfileImageUrl,
                    isActive = p.User.IsActive,
                    joinedAt = p.User.CreatedAt
                })
                .ToListAsync();

            var ids = rows.Select(r => r.patientId).ToList();
            var stats = ids.Count == 0
                ? new Dictionary<int, (int Count, DateTime? LastVisit)>()
                : (await _db.Appointments.AsNoTracking()
                    .Where(a => ids.Contains(a.PatientId))
                    .GroupBy(a => a.PatientId)
                    .Select(g => new
                    {
                        patientId = g.Key,
                        count = g.Count(),
                        lastVisit = g.Where(a => a.Status == "Completed")
                            .Max(a => (DateTime?)a.StartDateTime)
                    })
                    .ToListAsync())
                    .ToDictionary(x => x.patientId, x => (Count: x.count, LastVisit: x.lastVisit));

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var items = rows.Select(r =>
            {
                stats.TryGetValue(r.patientId, out var stat);
                return new
                {
                    id = r.patientId.ToString(),
                    userId = r.userId.ToString(),
                    fullName = r.fullName,
                    email = r.email,
                    phone = r.phone,
                    gender = r.gender,
                    dateOfBirth = r.dateOfBirth?.ToString("yyyy-MM-dd"),
                    age = r.dateOfBirth.HasValue ? AgeOn(r.dateOfBirth.Value, today) : (int?)null,
                    city = r.city,
                    profileImageUrl = r.profileImageUrl,
                    status = r.isActive ? "active" : "suspended",
                    isActive = r.isActive,
                    appointments = stat.Count,
                    lastVisit = Iso(stat.LastVisit),
                    joinedAt = Iso(r.joinedAt)
                };
            }).ToList();

            var genders = await _db.Patients.AsNoTracking()
                .Where(p => p.Gender != null)
                .Select(p => p.Gender!).Distinct().OrderBy(g => g).ToListAsync();

            //  قائمة المدن تملأ قائمة التصفية، فتأتي من كل المرضى لا من الصفحة
            var cities = await _db.Patients.AsNoTracking()
                .Where(p => p.City != null && p.City != "")
                .Select(p => p.City!)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();

            return Ok(new
            {
                items,
                total,
                filters = new { genders, cities }
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
        //  6a. PATIENT DETAIL   GET /api/admin/patients/{id}
        //  The list row only carries what the table shows; the edit dialog
        //  needs the profile fields too.
        // ─────────────────────────────────────────────────────────────

        [HttpGet("patients/{id:int}")]
        public async Task<IActionResult> GetPatient(int id)
        {
            var patient = await _db.Patients.AsNoTracking()
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.PatientId == id);

            if (patient == null)
                return ApiError.NotFound("not_found", "Patient not found.");

            return Ok(new
            {
                id = patient.PatientId.ToString(),
                fullName = patient.FullName,
                email = patient.User.Email,
                phone = patient.User.PhoneNumber,
                city = patient.City,
                gender = patient.Gender,
                dateOfBirth = patient.DateOfBirth?.ToString("yyyy-MM-dd"),
                profileImageUrl = patient.ProfileImageUrl,
                isActive = patient.User.IsActive,
                joinedAt = Iso(patient.User.CreatedAt)
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  6b. EDIT PATIENT   PUT /api/admin/patients/{id}
        // ─────────────────────────────────────────────────────────────

        [HttpPut("patients/{id:int}")]
        public async Task<IActionResult> UpdatePatient(int id, AdminUpdatePatientDto dto)
        {
            var patient = await _db.Patients
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.PatientId == id);

            if (patient == null)
                return ApiError.NotFound("not_found", "Patient not found.");

            if (string.IsNullOrWhiteSpace(dto.FullName))
                return ApiError.Validation(
                    new Dictionary<string, string[]> { ["FullName"] = ["Full name is required."] },
                    "Full name is required.");

            patient.FullName = dto.FullName.Trim();
            if (!string.IsNullOrWhiteSpace(dto.PhoneNumber))
                patient.User.PhoneNumber = dto.PhoneNumber.Trim();
            if (dto.City != null)
                patient.City = string.IsNullOrWhiteSpace(dto.City) ? null : dto.City.Trim();
            if (dto.Gender != null)
                patient.Gender = string.IsNullOrWhiteSpace(dto.Gender) ? null : dto.Gender.Trim();
            if (dto.DateOfBirth.HasValue)
                patient.DateOfBirth = dto.DateOfBirth.Value;

            patient.User.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                id = patient.PatientId.ToString(),
                name = patient.FullName,
                email = patient.User.Email,
                phone = patient.User.PhoneNumber,
                city = patient.City,
                gender = patient.Gender,
                dateOfBirth = patient.DateOfBirth?.ToString("yyyy-MM-dd")
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  6c. DELETE PATIENT   DELETE /api/admin/patients/{id}
        //  The account row alone is not enough: appointments, reviews,
        //  assessments, notes and saved rows all point at the patient, so they
        //  are removed in one transaction before the profile and login go.
        // ─────────────────────────────────────────────────────────────

        [HttpDelete("patients/{id:int}")]
        public async Task<IActionResult> DeletePatient(int id)
        {
            var patient = await _db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.PatientId == id);
            if (patient == null)
                return ApiError.NotFound("not_found", "Patient not found.");

            var userId = patient.UserId;
            var appointmentIds = await _db.Appointments.Where(a => a.PatientId == id)
                .Select(a => a.AppointmentId).ToListAsync();
            var assessmentIds = await _db.Assessments.Where(a => a.PatientId == id)
                .Select(a => a.AssessmentId).ToListAsync();
            var conversationIds = await _db.Conversations.Where(c => c.PatientId == id)
                .Select(c => c.ConversationId).ToListAsync();

            await using var tx = await _db.Database.BeginTransactionAsync();

            await _db.Reviews.Where(r => r.PatientId == id || appointmentIds.Contains(r.AppointmentId)).ExecuteDeleteAsync();
            await _db.AppointmentStatusHistories
                .Where(h => appointmentIds.Contains(h.AppointmentId) || h.ChangedByUserId == userId)
                .ExecuteDeleteAsync();
            await _db.Appointments.Where(a => a.PatientId == id).ExecuteDeleteAsync();
            await _db.AssessmentAnswers.Where(a => assessmentIds.Contains(a.AssessmentId)).ExecuteDeleteAsync();
            await _db.Assessments.Where(a => a.PatientId == id).ExecuteDeleteAsync();
            await _db.Messages.Where(m => conversationIds.Contains(m.ConversationId) || m.SenderUserId == userId).ExecuteDeleteAsync();
            await _db.Conversations.Where(c => c.PatientId == id).ExecuteDeleteAsync();
            await _db.PatientSavedCenters.Where(s => s.PatientId == id).ExecuteDeleteAsync();
            await _db.CenterPatients.Where(cp => cp.PatientId == id).ExecuteDeleteAsync();
            await _db.PatientNotes.Where(n => n.PatientId == id || n.AuthorUserId == userId).ExecuteDeleteAsync();
            await _db.Notifications.Where(n => n.UserId == userId).ExecuteDeleteAsync();
            await _db.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync();
            await _db.ExternalLogins.Where(e => e.UserId == userId).ExecuteDeleteAsync();
            await _db.PasswordResetTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync();
            await _db.Patients.Where(p => p.PatientId == id).ExecuteDeleteAsync();
            await _db.Users.Where(u => u.UserId == userId).ExecuteDeleteAsync();

            await tx.CommitAsync();

            return Ok(new { id = id.ToString(), deleted = true });
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

            var rows = await q
                .OrderByDescending(t => t.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new
                {
                    therapistId = t.TherapistId,
                    userId = t.UserId,
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
                    joinedAt = t.CreatedAt
                })
                .ToListAsync();

            //  تقييم المعالج الحقيقي = متوسط تقييمات المرضى الظاهرة لمواعيده
            //  (نفس قاعدة بيانات المركز العامة في CenterMapper)
            var ids = rows.Select(r => r.therapistId).ToList();
            var ratingRows = ids.Count == 0
                ? new List<(int TherapistId, int Count, double Average)>()
                : (await _db.Reviews.AsNoTracking()
                    .Where(r => r.IsVisible && ids.Contains(r.Appointment.TherapistId))
                    .GroupBy(r => r.Appointment.TherapistId)
                    .Select(g => new
                    {
                        therapistId = g.Key,
                        count = g.Count(),
                        average = g.Average(r => (double)r.Rating)
                    })
                    .ToListAsync())
                    .Select(x => (TherapistId: x.therapistId, Count: x.count, Average: x.average))
                    .ToList();
            var ratings = ratingRows.ToDictionary(
                x => x.TherapistId,
                x => (x.Count, Rating: (double?)Math.Round(x.Average, 1)));

            var items = rows.Select(r =>
            {
                ratings.TryGetValue(r.therapistId, out var rating);
                return new
                {
                    id = r.therapistId.ToString(),
                    userId = r.userId.ToString(),
                    fullName = r.fullName,
                    fullNameAr = r.fullNameAr,
                    email = r.email,
                    phone = r.phone,
                    center = r.center,
                    specialty = r.specialty,
                    bio = r.bio,
                    bioAr = r.bioAr,
                    yearsOfExperience = r.yearsOfExperience,
                    profileImageUrl = r.profileImageUrl,
                    rating = rating.Rating,
                    reviews = rating.Count,
                    isActive = r.isActive,
                    isAvailable = r.isAvailable,
                    joinedAt = Iso(r.joinedAt)
                };
            }).ToList();

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
        //  9a. THERAPIST DETAIL   GET /api/admin/physiotherapists/{id}
        // ─────────────────────────────────────────────────────────────

        [HttpGet("physiotherapists/{id:int}")]
        public async Task<IActionResult> GetTherapist(int id)
        {
            var therapist = await _db.Therapists.AsNoTracking()
                .Include(t => t.User)
                .Include(t => t.Center)
                .Include(t => t.Specialty)
                .FirstOrDefaultAsync(t => t.TherapistId == id);

            if (therapist == null)
                return ApiError.NotFound("not_found", "Physiotherapist not found.");

            return Ok(new
            {
                id = therapist.TherapistId.ToString(),
                fullName = therapist.FullName,
                fullNameAr = therapist.FullNameAr,
                email = therapist.User.Email,
                phone = therapist.User.PhoneNumber,
                centerId = therapist.CenterId.ToString(),
                centerName = therapist.Center.NameEnglish ?? therapist.Center.NameArabic,
                specialtyId = therapist.SpecialtyId.ToString(),
                specialtyName = therapist.Specialty.NameEnglish ?? therapist.Specialty.NameArabic,
                bio = therapist.Bio,
                yearsOfExperience = therapist.YearsOfExperience,
                profileImageUrl = therapist.ProfileImageUrl,
                isActive = therapist.IsActive,
                isAvailable = therapist.IsAvailable,
                joinedAt = Iso(therapist.CreatedAt)
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  9b. EDIT THERAPIST   PUT /api/admin/physiotherapists/{id}
        // ─────────────────────────────────────────────────────────────

        [HttpPut("physiotherapists/{id:int}")]
        public async Task<IActionResult> UpdateTherapist(int id, AdminUpdateTherapistDto dto)
        {
            var therapist = await _db.Therapists
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.TherapistId == id);

            if (therapist == null)
                return ApiError.NotFound("not_found", "Physiotherapist not found.");

            if (string.IsNullOrWhiteSpace(dto.FullName))
                return ApiError.Validation(
                    new Dictionary<string, string[]> { ["FullName"] = ["Full name is required."] },
                    "Full name is required.");

            if (dto.CenterId.HasValue && !await _db.Centers.AnyAsync(c => c.CenterId == dto.CenterId))
                return ApiError.NotFound("not_found", "Center not found.");

            if (dto.SpecialtyId.HasValue && !await _db.Specialties.AnyAsync(s => s.SpecialtyId == dto.SpecialtyId))
                return ApiError.NotFound("not_found", "Specialty not found.");

            therapist.FullName = dto.FullName.Trim();
            if (!string.IsNullOrWhiteSpace(dto.PhoneNumber))
                therapist.User.PhoneNumber = dto.PhoneNumber.Trim();
            if (dto.CenterId.HasValue)
                therapist.CenterId = dto.CenterId.Value;
            if (dto.SpecialtyId.HasValue)
                therapist.SpecialtyId = dto.SpecialtyId.Value;
            if (dto.YearsOfExperience.HasValue)
                therapist.YearsOfExperience = dto.YearsOfExperience.Value;
            if (dto.Bio != null)
                therapist.Bio = string.IsNullOrWhiteSpace(dto.Bio) ? null : dto.Bio.Trim();
            if (dto.IsAvailable.HasValue)
                therapist.IsAvailable = dto.IsAvailable.Value;

            therapist.User.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                id = therapist.TherapistId.ToString(),
                fullName = therapist.FullName,
                email = therapist.User.Email,
                phone = therapist.User.PhoneNumber,
                centerId = therapist.CenterId.ToString(),
                specialtyId = therapist.SpecialtyId.ToString(),
                yearsOfExperience = therapist.YearsOfExperience,
                bio = therapist.Bio,
                isActive = therapist.IsActive,
                isAvailable = therapist.IsAvailable
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  9c. DELETE THERAPIST   DELETE /api/admin/physiotherapists/{id}
        // ─────────────────────────────────────────────────────────────

        [HttpDelete("physiotherapists/{id:int}")]
        public async Task<IActionResult> DeleteTherapist(int id)
        {
            var therapist = await _db.Therapists.AsNoTracking().FirstOrDefaultAsync(t => t.TherapistId == id);
            if (therapist == null)
                return ApiError.NotFound("not_found", "Physiotherapist not found.");

            var userId = therapist.UserId;
            var appointmentIds = await _db.Appointments.Where(a => a.TherapistId == id)
                .Select(a => a.AppointmentId).ToListAsync();
            var conversationIds = await _db.Conversations.Where(c => c.TherapistId == id)
                .Select(c => c.ConversationId).ToListAsync();

            await using var tx = await _db.Database.BeginTransactionAsync();

            await _db.Reviews.Where(r => appointmentIds.Contains(r.AppointmentId)).ExecuteDeleteAsync();
            await _db.AppointmentStatusHistories
                .Where(h => appointmentIds.Contains(h.AppointmentId) || h.ChangedByUserId == userId)
                .ExecuteDeleteAsync();
            await _db.Appointments.Where(a => a.TherapistId == id).ExecuteDeleteAsync();
            await _db.Messages.Where(m => conversationIds.Contains(m.ConversationId) || m.SenderUserId == userId).ExecuteDeleteAsync();
            await _db.Conversations.Where(c => c.TherapistId == id).ExecuteDeleteAsync();
            await _db.TherapistServices.Where(ts => ts.TherapistId == id).ExecuteDeleteAsync();
            await _db.TherapistSchedules.Where(ts => ts.TherapistId == id).ExecuteDeleteAsync();
            await _db.PatientNotes.Where(n => n.AuthorUserId == userId).ExecuteDeleteAsync();
            await _db.Notifications.Where(n => n.UserId == userId).ExecuteDeleteAsync();
            await _db.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync();
            await _db.ExternalLogins.Where(e => e.UserId == userId).ExecuteDeleteAsync();
            await _db.PasswordResetTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync();
            await _db.Therapists.Where(t => t.TherapistId == id).ExecuteDeleteAsync();
            await _db.Users.Where(u => u.UserId == userId).ExecuteDeleteAsync();

            await tx.CommitAsync();

            return Ok(new { id = id.ToString(), deleted = true });
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
            // One exact day (YYYY-MM-DD) instead of a from/to range.
            [FromQuery] string? date,
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

            if (!string.IsNullOrWhiteSpace(date) &&
                DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                // Half-open range so the index on StartDateTime still applies.
                var dayStart = day.ToDateTime(TimeOnly.MinValue);
                var dayEnd = dayStart.AddDays(1);
                q = q.Where(a => a.StartDateTime >= dayStart && a.StartDateTime < dayEnd);
            }

            var total = await q.CountAsync();

            var items = await q
                .OrderByDescending(a => a.StartDateTime)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new
                {
                    id = a.AppointmentId.ToString(),
                    patient = new { id = a.PatientId.ToString(), name = a.Patient.FullName, profileImageUrl = a.Patient.ProfileImageUrl },
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

            // The status filter must offer exactly the values the column shows,
            // so the list comes from the table instead of a hardcoded set that
            // can drift from the real data.
            var storedStatuses = await _db.Appointments.AsNoTracking()
                .Select(a => a.Status)
                .Distinct()
                .ToListAsync();

            var canonicalOrder = new[] { "pending", "confirmed", "approved", "completed", "cancelled", "rejected" };
            var statuses = canonicalOrder
                .Where(s => storedStatuses.Any(v => string.Equals(v, s, StringComparison.OrdinalIgnoreCase)))
                .Concat(storedStatuses
                    .Select(v => v.ToLowerInvariant())
                    .Where(v => !canonicalOrder.Contains(v))
                    .Distinct()
                    .OrderBy(v => v))
                .ToList();

            var centersFilter = await _db.Centers.AsNoTracking()
                .Where(c => c.Status == "Approved")
                .Select(c => new { id = c.CenterId.ToString(), name = c.NameEnglish ?? c.NameArabic })
                .OrderBy(c => c.name)
                .ToListAsync();

            return Ok(new
            {
                items,
                total,
                filters = new { statuses, centers = centersFilter }
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

            var therapistRows = await _db.Therapists.AsNoTracking()
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

            //  خدمات كل معالج: قائمة الخدمة في نموذج الموعد تتبع المعالج المختار
            var serviceIdsByTherapist = (await _db.TherapistServices.AsNoTracking()
                    .Select(x => new { TherapistId = x.TherapistId, ServiceId = x.ServiceId })
                    .ToListAsync())
                .GroupBy(x => x.TherapistId.ToString())
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.ServiceId.ToString()).Distinct().ToList());

            var therapists = therapistRows
                .Select(t => new
                {
                    t.id,
                    t.name,
                    t.centerId,
                    t.specialty,
                    serviceIds = serviceIdsByTherapist.TryGetValue(t.id, out var ids)
                        ? ids
                        : new List<string>()
                })
                .ToList();

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
        //  12b. EDIT APPOINTMENT   PUT /api/admin/appointments/{id}
        // ─────────────────────────────────────────────────────────────

        [HttpPut("appointments/{id:int}")]
        public async Task<IActionResult> UpdateAppointment(int id, AdminUpdateAppointmentDto dto)
        {
            var appointment = await _db.Appointments
                .FirstOrDefaultAsync(a => a.AppointmentId == id);

            if (appointment == null)
                return ApiError.NotFound("not_found", "Appointment not found.");

            if (dto.CenterId.HasValue)
            {
                if (!await _db.Centers.AnyAsync(c => c.CenterId == dto.CenterId))
                    return ApiError.NotFound("not_found", "Center not found.");
                appointment.CenterId = dto.CenterId.Value;
            }

            if (dto.TherapistId.HasValue)
            {
                if (!await _db.Therapists.AnyAsync(t => t.TherapistId == dto.TherapistId))
                    return ApiError.NotFound("not_found", "Physiotherapist not found.");
                appointment.TherapistId = dto.TherapistId.Value;
            }

            if (dto.ServiceId.HasValue)
            {
                var service = await _db.Services.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.ServiceId == dto.ServiceId);
                if (service == null)
                    return ApiError.NotFound("not_found", "Service not found.");
                appointment.ServiceId = service.ServiceId;
                appointment.PriceAtBooking = service.Price;
            }

            if (dto.StartDateTime.HasValue)
                appointment.StartDateTime = dto.StartDateTime.Value;
            if (dto.EndDateTime.HasValue)
                appointment.EndDateTime = dto.EndDateTime.Value;

            if (appointment.EndDateTime <= appointment.StartDateTime)
                return ApiError.BadRequest("invalid_time", "EndDateTime must be after StartDateTime.");

            if (!string.IsNullOrWhiteSpace(dto.Status))
            {
                var s = dto.Status.Trim().ToLowerInvariant();
                var allowed = new[] { "pending", "confirmed", "approved", "completed", "cancelled", "rejected" };
                if (!allowed.Contains(s))
                    return ApiError.BadRequest("invalid_status",
                        "Status must be pending, confirmed, approved, completed, cancelled, or rejected.");

                if (!string.Equals(appointment.Status, s, StringComparison.OrdinalIgnoreCase))
                {
                    var stored = char.ToUpper(s[0]) + s[1..];
                    _db.AppointmentStatusHistories.Add(new AppointmentStatusHistory
                    {
                        AppointmentId = appointment.AppointmentId,
                        OldStatus = appointment.Status,
                        NewStatus = stored,
                        ChangedByUserId = CurrentUserId(),
                        ChangedAt = DateTime.UtcNow
                    });
                    appointment.Status = stored;
                }
            }

            if (dto.PatientNotes != null)
                appointment.PatientNotes = string.IsNullOrWhiteSpace(dto.PatientNotes)
                    ? null
                    : dto.PatientNotes.Trim();

            appointment.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var patientName = await _db.Patients.AsNoTracking()
                .Where(p => p.PatientId == appointment.PatientId)
                .Select(p => p.FullName)
                .FirstOrDefaultAsync();

            return Ok(new
            {
                id = appointment.AppointmentId.ToString(),
                patientId = appointment.PatientId.ToString(),
                patientName,
                centerId = appointment.CenterId.ToString(),
                therapistId = appointment.TherapistId.ToString(),
                serviceId = appointment.ServiceId.ToString(),
                startDateTime = Iso(appointment.StartDateTime),
                endDateTime = Iso(appointment.EndDateTime),
                price = appointment.PriceAtBooking,
                status = appointment.Status.ToLowerInvariant(),
                patientNotes = appointment.PatientNotes,
                updatedAt = Iso(appointment.UpdatedAt!.Value)
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  12c. DELETE APPOINTMENT   DELETE /api/admin/appointments/{id}
        // ─────────────────────────────────────────────────────────────

        [HttpDelete("appointments/{id:int}")]
        public async Task<IActionResult> DeleteAppointment(int id)
        {
            var appointment = await _db.Appointments.AsNoTracking()
                .FirstOrDefaultAsync(a => a.AppointmentId == id);

            if (appointment == null)
                return ApiError.NotFound("not_found", "Appointment not found.");

            await using var tx = await _db.Database.BeginTransactionAsync();

            // A review points at exactly one appointment and blocks its deletion.
            await _db.Reviews.Where(r => r.AppointmentId == id).ExecuteDeleteAsync();
            await _db.AppointmentStatusHistories.Where(h => h.AppointmentId == id).ExecuteDeleteAsync();
            await _db.Appointments.Where(a => a.AppointmentId == id).ExecuteDeleteAsync();

            await tx.CommitAsync();

            return Ok(new { id = id.ToString(), deleted = true });
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
            [FromQuery] int? reviewId,      //  تقييم واحد محدد (روابط سجل النشاط)
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

            if (reviewId.HasValue)
                q = q.Where(r => r.ReviewId == reviewId.Value);

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
                profileImageUrl = admin.ProfileImageUrl,
                // The profile card shows who the account is, so the role and the
                // account flag travel with the rest of the profile.
                role = admin.User.Role,
                isActive = admin.User.IsActive,
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

            //  الصورة تُحدَّث فقط عند إرسالها، ويمكن محوها بإرسال نص فارغ
            if (dto.ProfileImageUrl != null)
            {
                admin.ProfileImageUrl = string.IsNullOrWhiteSpace(dto.ProfileImageUrl)
                    ? null
                    : dto.ProfileImageUrl.Trim();
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
                profileImageUrl = admin.ProfileImageUrl,
                role = admin.User.Role,
                isActive = admin.User.IsActive,
                maintenanceMode = false
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  16b. ADMIN PROFILE IMAGE   POST /api/admin/settings/image
        //      multipart upload with the shared "file" field
        // ─────────────────────────────────────────────────────────────

        [HttpPost("settings/image")]
        [RequestSizeLimit(ImageStorage.MaxBytes + 1024 * 1024)]
        public async Task<IActionResult> UploadSettingsImage(
            IFormFile? file,
            [FromServices] IWebHostEnvironment environment)
        {
            var validation = ImageStorage.Validate(file);
            if (validation != null)
            {
                return validation;
            }

            var url = await ImageStorage.SaveAsync(file!, environment);
            return Ok(new { url });
        }
    }
}
