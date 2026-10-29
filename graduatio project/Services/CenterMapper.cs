using graduatio_project.Data;
using graduatio_project.Models;
using Microsoft.EntityFrameworkCore;

namespace graduatio_project.Services
{
    //  يبني كائن المركز بالحقول بالضبط كما ترسمها الواجهة
    //  يستقبل مجموعات مُحمّلة مسبقًا حتى لا نقع في استعلام N+1 عند عرض القائمة
    public static class CenterMapper
    {
        //  ن prefer الإنجليزية ثم نعود إلى العربية إذا كانت فارغة
        public static string? Pick(string? english, string? arabic)
            => string.IsNullOrWhiteSpace(english) ? arabic : english;

        private static List<string> SplitList(string? value)
            => string.IsNullOrWhiteSpace(value)
                ? new List<string>()
                : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .ToList();

        public static async Task<Dictionary<int, object>> BuildAsync(
            ApplicationDbContext context,
            IReadOnlyList<Center> centers)
        {
            var result = new Dictionary<int, object>();
            if (centers.Count == 0)
            {
                return result;
            }

            var ids = centers.Select(c => c.CenterId).ToList();

            var images = await context.CenterImages.AsNoTracking()
                .Where(i => ids.Contains(i.CenterId))
                .OrderBy(i => i.DisplayOrder)
                .ToListAsync();

            var services = await context.Services.AsNoTracking()
                .Where(s => ids.Contains(s.CenterId) && s.IsActive)
                .Include(s => s.Specialty)
                .ToListAsync();

            var therapists = await context.Therapists.AsNoTracking()
                .Where(t => ids.Contains(t.CenterId) && t.IsActive)
                .Include(t => t.Specialty)
                .ToListAsync();

            var therapistIds = therapists.Select(t => t.TherapistId).ToList();

            var links = await context.TherapistServices.AsNoTracking()
                .Where(x => therapistIds.Contains(x.TherapistId))
                .ToListAsync();

            var schedules = await context.TherapistSchedules.AsNoTracking()
                .Where(s => therapistIds.Contains(s.TherapistId) && s.IsAvailable)
                .ToListAsync();

            var workingHours = await context.CenterWorkingHours.AsNoTracking()
                .Where(w => ids.Contains(w.CenterId))
                .ToListAsync();

            var reviews = await context.Reviews.AsNoTracking()
                .Where(r => ids.Contains(r.CenterId) && r.IsVisible)
                .Include(r => r.Patient)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            var appointmentIds = reviews.Select(r => r.AppointmentId).Distinct().ToList();
            var appointmentTherapist = await context.Appointments.AsNoTracking()
                .Where(a => appointmentIds.Contains(a.AppointmentId))
                .Select(a => new { a.AppointmentId, a.TherapistId })
                .ToDictionaryAsync(a => a.AppointmentId, a => a.TherapistId);

            var today = DateTime.Now.DayOfWeek;
            var scheduleToday = schedules
                .Where(s => s.DayOfWeek == today)
                .Select(s => s.TherapistId)
                .ToHashSet();

            foreach (var center in centers)
            {
                var centerImages = images.Where(i => i.CenterId == center.CenterId).ToList();
                var centerServices = services.Where(s => s.CenterId == center.CenterId).ToList();
                var centerTherapists = therapists.Where(t => t.CenterId == center.CenterId).ToList();
                var centerHours = workingHours.Where(w => w.CenterId == center.CenterId).ToList();
                var centerReviews = reviews.Where(r => r.CenterId == center.CenterId).ToList();

                //  تقييم المعالج = متوسط التقييمات المرتبطة بمواعيده
                var therapistRatings = centerReviews
                    .Where(r => appointmentTherapist.ContainsKey(r.AppointmentId))
                    .GroupBy(r => appointmentTherapist[r.AppointmentId])
                    .ToDictionary(g => g.Key, g => (double?)Math.Round(g.Average(x => x.Rating), 1));

                var weekdays = centerHours
                    .Where(w => w.DayOfWeek != DayOfWeek.Friday
                        && w.OpeningTime.HasValue && w.ClosingTime.HasValue)
                    .ToList();
                var friday = centerHours.FirstOrDefault(w => w.DayOfWeek == DayOfWeek.Friday
                    && w.OpeningTime.HasValue && w.ClosingTime.HasValue);

                var primary = centerImages.FirstOrDefault(i => i.IsPrimary)
                    ?? centerImages.FirstOrDefault();

                result[center.CenterId] = new
                {
                    id = center.CenterId.ToString(),
                    name = Pick(center.NameEnglish, center.NameArabic),
                    imageUrl = primary?.ImageUrl ?? center.ProfileImageUrl,
                    gallery = centerImages.Select(i => i.ImageUrl).ToList(),
                    about = Pick(center.DescriptionEnglish, center.DescriptionArabic),
                    address = center.Address,
                    city = center.City,
                    hours = new
                    {
                        weekStart = weekdays.Count == 0
                            ? null
                            : weekdays.Select(w => w.OpeningTime!.Value).Min().ToString("HH:mm"),
                        weekEnd = weekdays.Count == 0
                            ? null
                            : weekdays.Select(w => w.ClosingTime!.Value).Max().ToString("HH:mm"),
                        fridayStart = friday?.OpeningTime?.ToString("HH:mm"),
                        fridayEnd = friday?.ClosingTime?.ToString("HH:mm")
                    },
                    phone = center.User?.PhoneNumber,
                    email = center.User?.Email,
                    services = centerServices.Select(s => new
                    {
                        id = s.ServiceId.ToString(),
                        name = Pick(s.NameEnglish, s.NameArabic),
                        description = Pick(s.DescriptionEnglish, s.DescriptionArabic),
                        price = s.Price,
                        duration = s.DurationMinutes,
                        imageUrl = (string?)null,
                        specialty = s.Specialty == null
                            ? null
                            : Pick(s.Specialty.NameEnglish, s.Specialty.NameArabic)
                    }).ToList(),
                    therapists = centerTherapists.Select(t => new
                    {
                        id = t.TherapistId.ToString(),
                        name = t.FullName,
                        imageUrl = t.ProfileImageUrl,
                        specialty = t.Specialty == null
                            ? null
                            : Pick(t.Specialty.NameEnglish, t.Specialty.NameArabic),
                        experience = t.YearsOfExperience ?? 0,
                        about = t.Bio,
                        rating = therapistRatings.TryGetValue(t.TherapistId, out var tr)
                            ? tr
                            : (double?)null,
                        serviceIds = links
                            .Where(l => l.TherapistId == t.TherapistId)
                            .Select(l => l.ServiceId.ToString())
                            .ToList(),
                        availability = t.IsAvailable && scheduleToday.Contains(t.TherapistId)
                            ? "available"
                            : "unavailable",
                        credentials = SplitList(t.Qualifications),
                        languages = SplitList(t.Languages)
                    }).ToList(),
                    reviews = centerReviews.Select(r => new
                    {
                        id = r.ReviewId.ToString(),
                        rating = r.Rating,
                        text = r.Comment,
                        date = r.CreatedAt.ToString("yyyy-MM-dd"),
                        reply = r.Reply,
                        author = r.Patient?.FullName,
                        authorImageUrl = r.Patient?.ProfileImageUrl
                    }).ToList(),
                    rating = centerReviews.Count == 0
                        ? (double?)null
                        : Math.Round(centerReviews.Average(r => r.Rating), 1)
                };
            }

            return result;
        }
    }
}