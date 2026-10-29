using graduatio_project.Data;
using graduatio_project.Models;
using Microsoft.EntityFrameworkCore;

namespace graduatio_project.Services
{
    //  نتيجة التحقق من (مركز + معالج + خدمة) قبل الحجز
    public sealed class BookingTarget
    {
        public Center Center { get; init; } = null!;
        public Therapist Therapist { get; init; } = null!;
        public Service Service { get; init; } = null!;
    }

    //  قواعد التوفر الوحيدة المسؤولة عن حساب المواعيد المتاحة
    //  نستخدمها في شاشة المواعيد وفي حجز الموعد نفسه
    //  حتى لا يختلف الحساب بين العرض والتنفيذ
    public static class BookingRules
    {
        //  خطوة الشبكة بالدقائق: ٣٠ دقيقة كما في العقد
        private const int StepMinutes = 30;

        //  حالات الموعد التي لا تحجز وقتًا فعليًا
        private static readonly string[] FreeStatuses = { "Cancelled", "Rejected" };

        //  نتحقق أن المركز معتمد وأن المعالج والخدمة يعنوان هذا المركز
        //  وأن المعالج يقدم الخدمة فعلًا؛ وإلا نرجع 404 بشفرة ثابتة
        public static async Task<(BookingTarget? target, string code, string message)> ResolveAsync(
            ApplicationDbContext context, int centerId, int therapistId, int serviceId)
        {
            var center = await context.Centers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.CenterId == centerId && c.Status == "Approved");

            if (center == null)
            {
                return (null, "not_found", "Center not found.");
            }

            var therapist = await context.Therapists.AsNoTracking()
                .FirstOrDefaultAsync(t => t.TherapistId == therapistId
                    && t.CenterId == centerId && t.IsActive);

            if (therapist == null)
            {
                return (null, "not_found", "Therapist not found in this center.");
            }

            var service = await context.Services.AsNoTracking()
                .FirstOrDefaultAsync(s => s.ServiceId == serviceId
                    && s.CenterId == centerId && s.IsActive);

            if (service == null)
            {
                return (null, "not_found", "Service not offered by this center.");
            }

            var offersService = await context.TherapistServices.AsNoTracking()
                .AnyAsync(x => x.TherapistId == therapistId && x.ServiceId == serviceId);

            if (!offersService)
            {
                return (null, "not_found", "This therapist does not offer the requested service.");
            }

            return (new BookingTarget { Center = center, Therapist = therapist, Service = service },
                string.Empty, string.Empty);
        }

        //  نحسب المواعيد المتاحة ليوم معيّن لمعالج وخدمة داخل مركز
        //  ترجع قائمة فارغة إذا كان المركز مغلقًا أو المعالج غير متاح في ذلك اليوم
        public static async Task<List<string>> ComputeSlotsAsync(
            ApplicationDbContext context,
            int centerId,
            int therapistId,
            int serviceId,
            DateOnly date,
            DateTime now)
        {
            var empty = new List<string>();

            //  ١) ساعات عمل المركز في ذلك اليوم
            var centerHour = await context.CenterWorkingHours.AsNoTracking()
                .FirstOrDefaultAsync(w => w.CenterId == centerId
                    && w.DayOfWeek == date.DayOfWeek);

            if (centerHour == null || centerHour.IsClosed
                || centerHour.OpeningTime == null || centerHour.ClosingTime == null)
            {
                return empty;
            }

            //  ٢) جدول المعالج في ذلك اليوم
            var schedule = await context.TherapistSchedules.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TherapistId == therapistId
                    && s.DayOfWeek == date.DayOfWeek && s.IsAvailable);

            if (schedule == null)
            {
                return empty;
            }

            //  ٣) نتقاطع نافذتي المركز والمعالج
            var windowStart = schedule.StartTime > centerHour.OpeningTime
                ? schedule.StartTime
                : centerHour.OpeningTime.Value;
            var windowEnd = schedule.EndTime < centerHour.ClosingTime
                ? schedule.EndTime
                : centerHour.ClosingTime.Value;

            if (windowStart >= windowEnd)
            {
                return empty;
            }

            //  ٤) مدة الخدمة تحدد طول كل موعد
            var duration = await context.Services.AsNoTracking()
                .Where(s => s.ServiceId == serviceId)
                .Select(s => s.DurationMinutes)
                .FirstOrDefaultAsync();

            if (duration <= 0)
            {
                duration = StepMinutes;
            }

            //  ٥) المواعيد المحجوزة فعليًا في ذلك اليوم
            var booked = await GetBookedAsync(context, therapistId, date);

            var dayStart = date.ToDateTime(TimeOnly.MinValue);

            var slots = new List<string>();
            for (var start = windowStart; start < windowEnd; start = start.AddMinutes(StepMinutes))
            {
                var end = start.AddMinutes(duration);
                if (end > windowEnd)
                {
                    break;
                }

                //  لا نعرض مواعيد في الماضي
                if (date.ToDateTime(start) <= now)
                {
                    continue;
                }

                //  لا نعرض موعدًا يتداخل مع حجز قائم
                var slotStart = start;
                var hasConflict = booked.Any(b => slotStart < b.end && end > b.start);
                if (hasConflict)
                {
                    continue;
                }

                slots.Add(start.ToString("HH:mm"));
            }

            return slots;
        }

        //  نبحث الموعد المطلوب ضمن المواعيد المتاحة فعليًا
        //  بهذه الطابة يتأكد الحجز بنفس منطق العرض تمامًا
        public static async Task<bool> IsSlotAvailableAsync(
            ApplicationDbContext context,
            int centerId,
            int therapistId,
            int serviceId,
            DateOnly date,
            TimeOnly time,
            DateTime now)
        {
            var slots = await ComputeSlotsAsync(context, centerId, therapistId, serviceId, date, now);
            return slots.Contains(time.ToString("HH:mm"));
        }

        //  كل مواعيد المعالج في اليوم المحدد التي تحجز وقتًا
        private static async Task<List<(TimeOnly start, TimeOnly end)>> GetBookedAsync(
            ApplicationDbContext context, int therapistId, DateOnly date)
        {
            var dayStart = date.ToDateTime(TimeOnly.MinValue);
            var dayEnd = dayStart.AddDays(1);

            var rows = await context.Appointments.AsNoTracking()
                .Where(a => a.TherapistId == therapistId
                    && a.StartDateTime >= dayStart
                    && a.StartDateTime < dayEnd
                    && !FreeStatuses.Contains(a.Status))
                .Select(a => new { a.StartDateTime, a.EndDateTime })
                .ToListAsync();

            return rows
                .Select(a => (
                    new TimeOnly(a.StartDateTime.Hour, a.StartDateTime.Minute, a.StartDateTime.Second),
                    new TimeOnly(a.EndDateTime.Hour, a.EndDateTime.Minute, a.EndDateTime.Second)))
                .ToList();
        }
    }
}
