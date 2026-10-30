using Microsoft.EntityFrameworkCore;
using graduatio_project.Models;
using graduatio_project.Services;

namespace graduatio_project.Data
{
    //  مسؤول عن ملء قاعدة البيانات ببيانات تجريبية عند تشغيلها لأول مرة
    //  لا يعمل إلا إذا كان جدول Users فارغًا حتى لا نمسح بيانات حقيقية
    public static class DbSeeder
    {
        public static async Task SeedAsync(ApplicationDbContext context, PasswordService passwordService)
        {
            //  نستخدم حساب الأدمن كعلامة على أن البيانات التجريبية موجودة أصلًا
            //  بهذا الشكل تعمل الدالة حتى على قاعدة فيها حسابات حقيقية بالفعل
            if (await context.Users.AnyAsync(u => u.Email == "admin@physioguide.dev"))
            {
                return;
            }

            //  نبدأ بجدول التخصصات لأنه مرجع لاحق للخدمات والمعالجين
            var specialties = new List<Specialty>
            {
                new Specialty { NameArabic = "العلاج الطبيعي العام", NameEnglish = "General Physiotherapy",
                    DescriptionArabic = "علاج عام للآلام الحركية", DescriptionEnglish = "General treatment for movement disorders" },
                new Specialty { NameArabic = "إصابات الرياضة", NameEnglish = "Sports Injury",
                    DescriptionArabic = "علاج إصابات الرياضيين", DescriptionEnglish = "Rehabilitation for sports injuries" },
                new Specialty { NameArabic = "إعادة التأهيل العصبي", NameEnglish = "Neurological Rehabilitation",
                    DescriptionArabic = "تأهيل الحالات العصبية", DescriptionEnglish = "Rehabilitation after neurological conditions" },
                new Specialty { NameArabic = "العلاج الطبيعي للأطفال", NameEnglish = "Pediatric Physiotherapy",
                    DescriptionArabic = "علاج طبيعي للأطفال", DescriptionEnglish = "Physiotherapy for children" }
            };
            context.Specialties.AddRange(specialties);
            await context.SaveChangesAsync();

            var general = specialties[0];
            var sports = specialties[1];
            var neuro = specialties[2];
            var pediatric = specialties[3];

            //  حساب الأدمن: طريقة الدخول الوحيدة لواجهة مراجعة طلبات المراكز
            var adminUser = await GetOrCreateUserAsync(context, passwordService,
                "admin@physioguide.dev", "+970599000001", "Admin123!", "Admin");

            if (!await context.Admins.AnyAsync(a => a.UserId == adminUser.UserId))
            {
                context.Admins.Add(new Admin { UserId = adminUser.UserId, FullName = "System Administrator" });
            }
            await context.SaveChangesAsync();

            //  ApprovedByAdminId يشير إلى Admins.AdminId وليس إلى Users.UserId
            var adminRecord = await context.Admins
                .FirstAsync(a => a.UserId == adminUser.UserId);

            //  حساب المريض التجريبي: يستخدم في كل الشاشات الخاصة بالمرضى
            var patientUser = await GetOrCreateUserAsync(context, passwordService,
                "patient@physioguide.dev", "+970599000002", "Patient123!", "Patient");

            var patient = await context.Patients
                .FirstOrDefaultAsync(p => p.UserId == patientUser.UserId);

            if (patient == null)
            {
                patient = new Patient
                {
                    UserId = patientUser.UserId,
                    FullName = "Sara Nablus",
                    Gender = "female",
                    DateOfBirth = new DateOnly(1996, 4, 12),
                    ProfileImageUrl = "https://i.pravatar.cc/160?img=47"
                };
                context.Patients.Add(patient);
            }
            await context.SaveChangesAsync();

            //  حساب مركز مسجل لكن لم يوافق عليه الأدمن بعد: يظهر في شاشة المراجعة
            var pendingCenterUser = await GetOrCreateUserAsync(context, passwordService,
                "pending-center@physioguide.dev", "+970599000003", "Center123!", "Center");

            if (!await context.Centers.AnyAsync(c => c.UserId == pendingCenterUser.UserId))
            {
                context.Centers.Add(new Center
                {
                    UserId = pendingCenterUser.UserId,
                    NameArabic = "مركز خليلي للتأهيل",
                    NameEnglish = "Khalili Rehab Center",
                    City = "Hebron",
                    Address = "Main Street, Hebron",
                    Status = "Pending"
                });
            }

            await context.SaveChangesAsync();

            //  مركزان معتمدان هما جوهر البيانات التجريبية
            var center1 = await CreateCenterAsync(context, passwordService,
                "مركز نابلس للفيزياء", "Nablus Physio Center", "Nablus",
                "شارع الزهراء، نابلس", "https://images.unsplash.com/photo-1571019613454-1cb2f99b2d8b",
                adminRecord.AdminId, "center@physioguide.dev", "+970599000004");

            var center2 = await CreateCenterAsync(context, passwordService,
                "مركز رام الله للتأهيل", "Ramallah Rehab Center", "Ramallah",
                "الجلد street, Ramallah", "https://images.unsplash.com/photo-1519494026892-80bbd2d6fd0d",
                adminRecord.AdminId, "center2@physioguide.dev", "+970599000005");

            //  مواعيد العمل: السبت إلى الخميس ٨-١٧ والجمعة ٨-١٣
            await AddWorkingHoursAsync(context, center1.CenterId);
            await AddWorkingHoursAsync(context, center2.CenterId);

            var service1a = AddService(context, center1, sports, "Sports Massage", "مساج رياضي", 150m, 30);
            var service1b = AddService(context, center1, general, "Joint Mobilization", "تحريك المفاصل", 200m, 45);
            var service1c = AddService(context, center1, general, "Post-Surgery Rehab", "تأهيل ما بعد العمليات", 300m, 60);

            var service2a = AddService(context, center2, neuro, "Neuro Rehab Session", "جلسة تأهيل عصبي", 350m, 60);
            var service2b = AddService(context, center2, pediatric, "Pediatric Movement Therapy", "علاج حركي للأطفال", 180m, 30);
            var service2c = AddService(context, center2, general, "Gait Training", "تدريب المشي", 250m, 45);
            await context.SaveChangesAsync();

            var therapist1a = await CreateTherapistAsync(context, passwordService, center1, sports,
                "Dr. Lina Khoury", "lina.khoury@physioguide.dev", "+970599000011",
                "Specialist in sports injuries with 7 years of clinical experience.",
                7, "BPT, Certified Sports Physiotherapist", "Arabic, English, French",
                new[] { service1a.ServiceId, service1b.ServiceId });

            var therapist1b = await CreateTherapistAsync(context, passwordService, center1, general,
                "Dr. Omar Haddad", "omar.haddad@physioguide.dev", "+970599000012",
                "Post-surgical rehabilitation and manual therapy.",
                12, "MSc Physiotherapy", "Arabic, English",
                new[] { service1b.ServiceId, service1c.ServiceId });

            var therapist2a = await CreateTherapistAsync(context, passwordService, center2, neuro,
                "Dr. Rania Saleh", "rania.saleh@physioguide.dev", "+970599000013",
                "Neurological rehabilitation after stroke and spinal injury.",
                9, "PhD Neurorehabilitation", "Arabic, English",
                new[] { service2a.ServiceId, service2c.ServiceId });

            var therapist2b = await CreateTherapistAsync(context, passwordService, center2, pediatric,
                "Dr. Khaled Amer", "khaled.amer@physioguide.dev", "+970599000014",
                "Pediatric development and cerebral palsy therapy.",
                6, "BPT, Pediatric Certification", "Arabic",
                new[] { service2b.ServiceId, service2c.ServiceId });

            await context.SaveChangesAsync();

            //  مواعيد المعالجين: السبت إلى الخميس ٩-١٦ ومتاحون
            foreach (var t in new[] { therapist1a, therapist1b, therapist2a, therapist2b })
            {
                await AddScheduleAsync(context, t.TherapistId);
            }

            //  الموعد المعتمد المستقبلي (الذي سيراه المريض في لوحته)
            var approvedAppointment = await AddAppointmentAsync(context, patient.PatientId, center1,
                therapist1a, service1a, DateTime.UtcNow.Date.AddDays(3), "09:30", "Approved");

            //  الموعد المعلق المطلوب
            await AddAppointmentAsync(context, patient.PatientId, center2,
                therapist2a, service2a, DateTime.UtcNow.Date.AddDays(5), "10:00", "Pending");

            //  مواعيد مكتملة قديمة: كل مراجعة مرتبطة بموعد خاص بها
            await AddAppointmentAsync(context, patient.PatientId, center1,
                therapist1a, service1a, DateTime.UtcNow.Date.AddDays(-40), "09:00", "Completed");
            var rev1 = await AddAppointmentAsync(context, patient.PatientId, center1,
                therapist1b, service1b, DateTime.UtcNow.Date.AddDays(-25), "11:00", "Completed");
            var rev2 = await AddAppointmentAsync(context, patient.PatientId, center1,
                therapist1a, service1c, DateTime.UtcNow.Date.AddDays(-12), "13:00", "Completed");

            var rev3 = await AddAppointmentAsync(context, patient.PatientId, center2,
                therapist2a, service2a, DateTime.UtcNow.Date.AddDays(-30), "10:00", "Completed");
            var rev4 = await AddAppointmentAsync(context, patient.PatientId, center2,
                therapist2b, service2b, DateTime.UtcNow.Date.AddDays(-18), "12:00", "Completed");
            var rev5 = await AddAppointmentAsync(context, patient.PatientId, center2,
                therapist2a, service2c, DateTime.UtcNow.Date.AddDays(-6), "14:00", "Completed");

            context.Reviews.AddRange(
                new Review
                {
                    PatientId = patient.PatientId, CenterId = center1.CenterId,
                    AppointmentId = rev1.AppointmentId, Rating = 5, IsVisible = true,
                    Comment = "Very professional team, the pain went away after three sessions.",
                    CreatedAt = DateTime.UtcNow.AddDays(-24),
                    Reply = "Thank you Sara, we are glad you are feeling better.",
                    RepliedAt = DateTime.UtcNow.AddDays(-23)
                },
                new Review
                {
                    PatientId = patient.PatientId, CenterId = center1.CenterId,
                    AppointmentId = rev2.AppointmentId, Rating = 4, IsVisible = true,
                    Comment = "Good results, though the waiting time was a bit long.",
                    CreatedAt = DateTime.UtcNow.AddDays(-11)
                },
                new Review
                {
                    PatientId = patient.PatientId, CenterId = center2.CenterId,
                    AppointmentId = rev3.AppointmentId, Rating = 5, IsVisible = true,
                    Comment = "Excellent neurological rehabilitation programme.",
                    CreatedAt = DateTime.UtcNow.AddDays(-29)
                },
                new Review
                {
                    PatientId = patient.PatientId, CenterId = center2.CenterId,
                    AppointmentId = rev4.AppointmentId, Rating = 4, IsVisible = true,
                    Comment = "My son enjoyed the sessions and improved a lot.",
                    CreatedAt = DateTime.UtcNow.AddDays(-17)
                },
                new Review
                {
                    PatientId = patient.PatientId, CenterId = center2.CenterId,
                    AppointmentId = rev5.AppointmentId, Rating = 5, IsVisible = true,
                    Comment = "Clean, organised and very helpful staff.",
                    CreatedAt = DateTime.UtcNow.AddDays(-5)
                }
            );

            //  محادثة واحدة بأربع رسائل حتى شاشة الرسائل ليست فارغة
            var conversation = new Conversation
            {
                PatientId = patient.PatientId,
                TherapistId = therapist1a.TherapistId,
                CreatedAt = DateTime.UtcNow.AddDays(-3)
            };
            context.Conversations.Add(conversation);
            await context.SaveChangesAsync();

            context.Messages.AddRange(
                new Message { ConversationId = conversation.ConversationId, SenderUserId = patientUser.UserId,
                    MessageText = "Hello doctor, I would like to ask about my exercises.", SentAt = DateTime.UtcNow.AddDays(-3) },
                new Message { ConversationId = conversation.ConversationId, SenderUserId = therapist1a.UserId,
                    MessageText = "Hello Sara, do the stretching exercises twice a day.", IsRead = true,
                    SentAt = DateTime.UtcNow.AddDays(-3).AddHours(2) },
                new Message { ConversationId = conversation.ConversationId, SenderUserId = patientUser.UserId,
                    MessageText = "Should I continue after the appointment?", SentAt = DateTime.UtcNow.AddDays(-2) },
                new Message { ConversationId = conversation.ConversationId, SenderUserId = therapist1a.UserId,
                    MessageText = "Yes, until our next session. Feel free to send me any update.",
                    SentAt = DateTime.UtcNow.AddDays(-1) }
            );

            context.Notifications.AddRange(
                new Notification
                {
                    UserId = patientUser.UserId, Type = "appointment",
                    Title = "Appointment confirmed",
                    Message = "Your appointment at Nablus Physio Center is confirmed.",
                    RelatedEntityId = approvedAppointment.AppointmentId
                },
                new Notification
                {
                    UserId = patientUser.UserId, Type = "message",
                    Title = "New message",
                    Message = "Dr. Lina Khoury sent you a new message.",
                    RelatedEntityId = conversation.ConversationId
                }
            );

            //  تفضيلات الإشعارات الافتراضية للمريض
            context.NotificationPreferences.Add(new NotificationPreference
            {
                UserId = patientUser.UserId,
                EmailEnabled = true,
                InAppEnabled = true,
                AppointmentReminders = true,
                Marketing = false,
                Language = "en"
            });

            await context.SaveChangesAsync();
        }

        //  ننشئ مستخدمًا إذا لم يكن بريده مسجلاً أصلًا
        //  بهذا الشكل تعمل الدالة على أي قاعدة بدون تكرار أو كسر قيد البريد الفريد
        private static async Task<User> GetOrCreateUserAsync(
            ApplicationDbContext context, PasswordService passwordService,
            string email, string phone, string password, string role)
        {
            var existing = await context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (existing != null)
            {
                return existing;
            }

            var user = new User
            {
                Email = email,
                PhoneNumber = phone,
                PasswordHash = passwordService.HashPassword(password),
                Role = role,
                IsActive = true
            };
            context.Users.Add(user);
            await context.SaveChangesAsync();
            return user;
        }

        private static async Task<Center> CreateCenterAsync(
            ApplicationDbContext context, PasswordService passwordService,
            string nameArabic, string nameEnglish, string city, string address,
            string imageUrl, int adminId, string email, string phone)
        {
            var user = await GetOrCreateUserAsync(context, passwordService,
                email, phone, "Center123!", "Center");

            var existing = await context.Centers
                .FirstOrDefaultAsync(c => c.UserId == user.UserId);
            if (existing != null)
            {
                return existing;
            }

            var center = new Center
            {
                UserId = user.UserId,
                NameArabic = nameArabic,
                NameEnglish = nameEnglish,
                DescriptionArabic = "مركز متخصص في العلاج الطبيعي والتأهيل.",
                DescriptionEnglish = "A specialised physiotherapy and rehabilitation centre with an experienced clinical team.",
                City = city,
                Address = address,
                ProfileImageUrl = imageUrl,
                WebsiteUrl = "https://physioguide.example",
                Status = "Approved",
                ApprovedByAdminId = adminId,
                ApprovedAt = DateTime.UtcNow.AddDays(-60),
                ReviewedAt = DateTime.UtcNow.AddDays(-60),
                LocationConfirmed = true,
                Latitude = city == "Nablus" ? 32.2211 : 31.9038,
                Longitude = city == "Nablus" ? 35.2544 : 35.2064
            };
            context.Centers.Add(center);
            await context.SaveChangesAsync();

            //  أربع صور لكل مركز: الأولى هي الصورة الرئيسية
            for (var i = 0; i < 4; i++)
            {
                context.CenterImages.Add(new CenterImage
                {
                    CenterId = center.CenterId,
                    ImageUrl = $"{imageUrl}?sig={i + 1}",
                    IsPrimary = i == 0,
                    DisplayOrder = i
                });
            }
            await context.SaveChangesAsync();

            return center;
        }

        private static async Task AddWorkingHoursAsync(ApplicationDbContext context, int centerId)
        {
            //  السبت (6) حتى الخميس (4) ساعات العمل نفسها
            for (var i = 0; i <= 4; i++)
            {
                context.CenterWorkingHours.Add(new CenterWorkingHour
                {
                    CenterId = centerId,
                    DayOfWeek = (DayOfWeek)i,
                    OpeningTime = new TimeOnly(8, 0),
                    ClosingTime = new TimeOnly(17, 0),
                    IsClosed = false
                });
            }

            //  الجمعة (5) نصف يوم
            context.CenterWorkingHours.Add(new CenterWorkingHour
            {
                CenterId = centerId,
                DayOfWeek = DayOfWeek.Friday,
                OpeningTime = new TimeOnly(8, 0),
                ClosingTime = new TimeOnly(13, 0),
                IsClosed = false
            });

            //  السبت (6)
            context.CenterWorkingHours.Add(new CenterWorkingHour
            {
                CenterId = centerId,
                DayOfWeek = DayOfWeek.Saturday,
                OpeningTime = new TimeOnly(8, 0),
                ClosingTime = new TimeOnly(17, 0),
                IsClosed = false
            });

            await context.SaveChangesAsync();
        }

        private static Service AddService(ApplicationDbContext context, Center center, Specialty specialty,
            string nameEnglish, string nameArabic, decimal price, int duration)
        {
            var service = new Service
            {
                CenterId = center.CenterId,
                SpecialtyId = specialty.SpecialtyId,
                NameEnglish = nameEnglish,
                NameArabic = nameArabic,
                DescriptionEnglish = "Professional physiotherapy session.",
                DescriptionArabic = "جلسة علاج طبيعي احترافية.",
                Price = price,
                DurationMinutes = duration,
                IsActive = true
            };
            context.Services.Add(service);
            return service;
        }

        private static async Task<Therapist> CreateTherapistAsync(
            ApplicationDbContext context, PasswordService passwordService,
            Center center, Specialty specialty, string fullName, string email, string phone,
            string bio, int years, string qualifications, string languages, int[] serviceIds)
        {
            var user = await GetOrCreateUserAsync(context, passwordService,
                email, phone, "Therapist123!", "Therapist");

            var existing = await context.Therapists
                .FirstOrDefaultAsync(t => t.UserId == user.UserId);
            if (existing != null)
            {
                return existing;
            }

            var therapist = new Therapist
            {
                UserId = user.UserId,
                CenterId = center.CenterId,
                SpecialtyId = specialty.SpecialtyId,
                FullName = fullName,
                Bio = bio,
                ProfileImageUrl = $"https://i.pravatar.cc/240?u={email}",
                YearsOfExperience = years,
                Qualifications = qualifications,
                Languages = languages,
                IsAvailable = true,
                IsActive = true
            };
            context.Therapists.Add(therapist);
            await context.SaveChangesAsync();

            foreach (var serviceId in serviceIds)
            {
                context.TherapistServices.Add(new TherapistService
                {
                    TherapistId = therapist.TherapistId,
                    ServiceId = serviceId
                });
            }
            await context.SaveChangesAsync();

            return therapist;
        }

        private static async Task AddScheduleAsync(ApplicationDbContext context, int therapistId)
        {
            //  السبت (6) حتى الخميس (4): 09:00 - 16:00
            for (var i = 0; i <= 4; i++)
            {
                context.TherapistSchedules.Add(new TherapistSchedule
                {
                    TherapistId = therapistId,
                    DayOfWeek = (DayOfWeek)i,
                    StartTime = new TimeOnly(9, 0),
                    EndTime = new TimeOnly(16, 0),
                    IsAvailable = true
                });
            }

            context.TherapistSchedules.Add(new TherapistSchedule
            {
                TherapistId = therapistId,
                DayOfWeek = DayOfWeek.Saturday,
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(16, 0),
                IsAvailable = true
            });

            await context.SaveChangesAsync();
        }

        private static async Task<Appointment> AddAppointmentAsync(
            ApplicationDbContext context, int patientId, Center center, Therapist therapist,
            Service service, DateTime date, string time, string status)
        {
            var start = date.Date.AddHours(int.Parse(time.Substring(0, 2))).AddMinutes(int.Parse(time.Substring(3, 2)));

            var appointment = new Appointment
            {
                PatientId = patientId,
                CenterId = center.CenterId,
                TherapistId = therapist.TherapistId,
                ServiceId = service.ServiceId,
                StartDateTime = start,
                EndDateTime = start.AddMinutes(service.DurationMinutes),
                PriceAtBooking = service.Price,
                Status = status,
                PatientNotes = "Routine session.",
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            };
            context.Appointments.Add(appointment);
            await context.SaveChangesAsync();
            return appointment;
        }
    }
}
