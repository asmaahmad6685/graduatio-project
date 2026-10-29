using graduatio_project.Data;
using graduatio_project.Models;
using Microsoft.EntityFrameworkCore;

namespace graduatio_project.Services
{
    //  يبني عنصر الموعد بالحقول التي تعرضها كل الشاشات
    //  المعرفات تعود نصوصًا كما اتفقنا في العقد
    public static class AppointmentMapper
    {
        public static List<object> Build(
            IEnumerable<Appointment> appointments,
            IReadOnlyDictionary<int, Center> centers,
            IReadOnlyDictionary<int, Therapist> therapists,
            IReadOnlyDictionary<int, Service> services)
        {
            var list = new List<object>();

            foreach (var a in appointments)
            {
                centers.TryGetValue(a.CenterId, out var center);
                therapists.TryGetValue(a.TherapistId, out var therapist);
                services.TryGetValue(a.ServiceId, out var service);

                list.Add(new
                {
                    id = a.AppointmentId.ToString(),
                    patientId = a.PatientId.ToString(),
                    therapistId = a.TherapistId.ToString(),
                    serviceId = a.ServiceId.ToString(),
                    date = a.StartDateTime.ToString("yyyy-MM-dd"),
                    time = a.StartDateTime.ToString("HH:mm"),
                    status = a.Status.ToLowerInvariant(),
                    duration = (int)Math.Round((a.EndDateTime - a.StartDateTime).TotalMinutes),
                    therapistImage = therapist?.ProfileImageUrl,
                    therapistAvailable = therapist?.IsAvailable ?? false,
                    centerId = a.CenterId.ToString(),
                    centerName = center == null
                        ? null
                        : CenterMapper.Pick(center.NameEnglish, center.NameArabic),
                    serviceName = service == null
                        ? null
                        : CenterMapper.Pick(service.NameEnglish, service.NameArabic),
                    therapistName = therapist?.FullName
                });
            }

            return list;
        }

        //  نجلب الكيانات المرجعية دفعة واحدة بدل استعلام لكل موعد
        public static async Task<(List<Appointment> appointments,
            Dictionary<int, Center> centers,
            Dictionary<int, Therapist> therapists,
            Dictionary<int, Service> services)> LoadAsync(
            Data.ApplicationDbContext context,
            IQueryable<Appointment> query)
        {
            var appointments = await query
                .AsNoTracking()
                .OrderByDescending(a => a.StartDateTime)
                .ToListAsync();

            var centerIds = appointments.Select(a => a.CenterId).Distinct().ToList();
            var therapistIds = appointments.Select(a => a.TherapistId).Distinct().ToList();
            var serviceIds = appointments.Select(a => a.ServiceId).Distinct().ToList();

            var centers = await context.Centers.AsNoTracking()
                .Where(c => centerIds.Contains(c.CenterId))
                .ToDictionaryAsync(c => c.CenterId);

            var therapists = await context.Therapists.AsNoTracking()
                .Where(t => therapistIds.Contains(t.TherapistId))
                .ToDictionaryAsync(t => t.TherapistId);

            var services = await context.Services.AsNoTracking()
                .Where(s => serviceIds.Contains(s.ServiceId))
                .ToDictionaryAsync(s => s.ServiceId);

            return (appointments, centers, therapists, services);
        }
    }
}