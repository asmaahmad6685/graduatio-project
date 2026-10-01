using Microsoft.EntityFrameworkCore;
using graduatio_project.Models;

namespace graduatio_project.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Patient> Patients { get; set; }
        public DbSet<Center> Centers { get; set; }
        public DbSet<Admin> Admins { get; set; }

        public DbSet<Specialty> Specialties { get; set; }
        public DbSet<Service> Services { get; set; }
        public DbSet<Therapist> Therapists { get; set; }
        public DbSet<TherapistService> TherapistServices { get; set; }

        public DbSet<CenterWorkingHour> CenterWorkingHours { get; set; }
        public DbSet<TherapistSchedule> TherapistSchedules { get; set; }
        public DbSet<CenterImage> CenterImages { get; set; }
        public DbSet<CenterDocument> CenterDocuments { get; set; }

        public DbSet<Appointment> Appointments { get; set; }
        public DbSet<AppointmentStatusHistory> AppointmentStatusHistories { get; set; }

        public DbSet<Review> Reviews { get; set; }
        public DbSet<Notification> Notifications { get; set; }

        public DbSet<AssessmentQuestion> AssessmentQuestions { get; set; }
        public DbSet<AssessmentOption> AssessmentOptions { get; set; }
        public DbSet<Assessment> Assessments { get; set; }
        public DbSet<AssessmentAnswer> AssessmentAnswers { get; set; }

        public DbSet<Conversation> Conversations { get; set; }
        public DbSet<Message> Messages { get; set; }

        public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Patient>()
                .HasOne(p => p.User)
                .WithOne()
                .HasForeignKey<Patient>(p => p.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Center>()
                .HasOne(c => c.User)
                .WithOne()
                .HasForeignKey<Center>(c => c.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Admin>()
                .HasOne(a => a.User)
                .WithOne()
                .HasForeignKey<Admin>(a => a.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Center -> Services (One-to-Many)
            modelBuilder.Entity<Service>()
                .HasOne(s => s.Center)
                .WithMany()
                .HasForeignKey(s => s.CenterId)
                .OnDelete(DeleteBehavior.Restrict);

            // Center -> Therapists (One-to-Many)
            modelBuilder.Entity<Therapist>()
                .HasOne(t => t.Center)
                .WithMany()
                .HasForeignKey(t => t.CenterId)
                .OnDelete(DeleteBehavior.Restrict);

            // Center -> Images (One-to-Many)
            modelBuilder.Entity<CenterImage>()
                .HasOne(i => i.Center)
                .WithMany()
                .HasForeignKey(i => i.CenterId)
                .OnDelete(DeleteBehavior.Cascade);

            // Center -> Documents (One-to-Many)
            modelBuilder.Entity<CenterDocument>()
                .HasOne(d => d.Center)
                .WithMany()
                .HasForeignKey(d => d.CenterId)
                .OnDelete(DeleteBehavior.Cascade);

            // Center -> Working Hours (One-to-Many)
            modelBuilder.Entity<CenterWorkingHour>()
                .HasOne(w => w.Center)
                .WithMany()
                .HasForeignKey(w => w.CenterId)
                .OnDelete(DeleteBehavior.Cascade);

            // Admin -> Approved Centers (One-to-Many)
            modelBuilder.Entity<Center>()
                .HasOne(c => c.ApprovedByAdmin)
                .WithMany()
                .HasForeignKey(c => c.ApprovedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);

            // Specialty -> Services (One-to-Many)
            modelBuilder.Entity<Service>()
                .HasOne(s => s.Specialty)
                .WithMany()
                .HasForeignKey(s => s.SpecialtyId)
                .OnDelete(DeleteBehavior.Restrict);

            // Specialty -> Therapists (One-to-Many)
            modelBuilder.Entity<Therapist>()
                .HasOne(t => t.Specialty)
                .WithMany()
                .HasForeignKey(t => t.SpecialtyId)
                .OnDelete(DeleteBehavior.Restrict);

            // TherapistService Composite Primary Key
            modelBuilder.Entity<TherapistService>()
                .HasKey(ts => new { ts.TherapistId, ts.ServiceId });

            // Therapist -> TherapistServices
            modelBuilder.Entity<TherapistService>()
                .HasOne(ts => ts.Therapist)
                .WithMany()
                .HasForeignKey(ts => ts.TherapistId)
                .OnDelete(DeleteBehavior.Restrict);

            // Service -> TherapistServices
            modelBuilder.Entity<TherapistService>()
                .HasOne(ts => ts.Service)
                .WithMany()
                .HasForeignKey(ts => ts.ServiceId)
                .OnDelete(DeleteBehavior.Restrict);

            // Therapist -> Schedules (One-to-Many)
            modelBuilder.Entity<TherapistSchedule>()
                .HasOne(ts => ts.Therapist)
                .WithMany()
                .HasForeignKey(ts => ts.TherapistId)
                .OnDelete(DeleteBehavior.Cascade);


            // Patient -> Appointments (One-to-Many)
            modelBuilder.Entity<Appointment>()
                .HasOne(a => a.Patient)
                .WithMany()
                .HasForeignKey(a => a.PatientId)
                .OnDelete(DeleteBehavior.Restrict);


            // Center -> Appointments (One-to-Many)
            modelBuilder.Entity<Appointment>()
                .HasOne(a => a.Center)
                .WithMany()
                .HasForeignKey(a => a.CenterId)
                .OnDelete(DeleteBehavior.Restrict);


            // Therapist -> Appointments (One-to-Many)
            modelBuilder.Entity<Appointment>()
                .HasOne(a => a.Therapist)
                .WithMany()
                .HasForeignKey(a => a.TherapistId)
                .OnDelete(DeleteBehavior.Restrict);


            // Service -> Appointments (One-to-Many)
            modelBuilder.Entity<Appointment>()
                .HasOne(a => a.Service)
                .WithMany()
                .HasForeignKey(a => a.ServiceId)
                .OnDelete(DeleteBehavior.Restrict);

            // Appointment -> Status History (One-to-Many)
            modelBuilder.Entity<AppointmentStatusHistory>()
                .HasOne(h => h.Appointment)
                .WithMany()
                .HasForeignKey(h => h.AppointmentId)
                .OnDelete(DeleteBehavior.Cascade);


            // User -> Appointment Status History (One-to-Many)
            modelBuilder.Entity<AppointmentStatusHistory>()
                .HasOne(h => h.ChangedByUser)
                .WithMany()
                .HasForeignKey(h => h.ChangedByUserId)
                .OnDelete(DeleteBehavior.Restrict);


            // Patient -> Reviews (One-to-Many)
            modelBuilder.Entity<Review>()
                .HasOne(r => r.Patient)
                .WithMany()
                .HasForeignKey(r => r.PatientId)
                .OnDelete(DeleteBehavior.Restrict);


            // Center -> Reviews (One-to-Many)
            modelBuilder.Entity<Review>()
                .HasOne(r => r.Center)
                .WithMany()
                .HasForeignKey(r => r.CenterId)
                .OnDelete(DeleteBehavior.Restrict);


            // Appointment -> Review (One-to-One)
            modelBuilder.Entity<Review>()
                .HasOne(r => r.Appointment)
                .WithOne()
                .HasForeignKey<Review>(r => r.AppointmentId)
                .OnDelete(DeleteBehavior.Restrict);

            // Question -> Options (One-to-Many)
            modelBuilder.Entity<AssessmentOption>()
                .HasOne(o => o.Question)
                .WithMany()
                .HasForeignKey(o => o.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);


            // Specialty -> Assessment Options (One-to-Many)
            modelBuilder.Entity<AssessmentOption>()
                .HasOne(o => o.Specialty)
                .WithMany()
                .HasForeignKey(o => o.SpecialtyId)
                .OnDelete(DeleteBehavior.Restrict);


            // Patient -> Assessments (One-to-Many)
            modelBuilder.Entity<Assessment>()
                .HasOne(a => a.Patient)
                .WithMany()
                .HasForeignKey(a => a.PatientId)
                .OnDelete(DeleteBehavior.Restrict);


            // Specialty -> Assessments (One-to-Many)
            modelBuilder.Entity<Assessment>()
                .HasOne(a => a.SuggestedSpecialty)
                .WithMany()
                .HasForeignKey(a => a.SuggestedSpecialtyId)
                .OnDelete(DeleteBehavior.Restrict);

            // AssessmentQuestion Constraints
            modelBuilder.Entity<AssessmentQuestion>()
                .Property(q => q.QuestionArabic)
                .HasMaxLength(500)
                .IsRequired();

            modelBuilder.Entity<AssessmentQuestion>()
                .Property(q => q.QuestionEnglish)
                .HasMaxLength(500)
            .IsRequired();



            // AssessmentOption Constraints
            modelBuilder.Entity<AssessmentOption>()
                .Property(o => o.TextArabic)
                .HasMaxLength(300)
                .IsRequired();

            modelBuilder.Entity<AssessmentOption>()
                .Property(o => o.TextEnglish)
                .HasMaxLength(300)
             .IsRequired();

            // AssessmentQuestion Primary Key
            modelBuilder.Entity<AssessmentQuestion>()
                .HasKey(q => q.QuestionId);

            // AssessmentOption Primary Key
            modelBuilder.Entity<AssessmentOption>()
                .HasKey(o => o.OptionId);

            // Assessment -> Answers (One-to-Many)
            modelBuilder.Entity<AssessmentAnswer>()
                .HasOne(a => a.Assessment)
                .WithMany()
                .HasForeignKey(a => a.AssessmentId)
                .OnDelete(DeleteBehavior.Cascade);


            // Question -> Assessment Answers (One-to-Many)
            modelBuilder.Entity<AssessmentAnswer>()
                .HasOne(a => a.Question)
                .WithMany()
                .HasForeignKey(a => a.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);


            // Option -> Assessment Answers (One-to-Many)
            modelBuilder.Entity<AssessmentAnswer>()
                .HasOne(a => a.Option)
                .WithMany()
                .HasForeignKey(a => a.OptionId)
                .OnDelete(DeleteBehavior.Restrict);

            // User -> Notifications (One-to-Many)
            modelBuilder.Entity<Notification>()
                .HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Restrict);


            // Patient -> Conversations (One-to-Many)
            modelBuilder.Entity<Conversation>()
                .HasOne(c => c.Patient)
                .WithMany()
                .HasForeignKey(c => c.PatientId)
                .OnDelete(DeleteBehavior.Restrict);


            // Center -> Conversations (One-to-Many)
            modelBuilder.Entity<Conversation>()
                .HasOne(c => c.Center)
                .WithMany()
                .HasForeignKey(c => c.CenterId)
                .OnDelete(DeleteBehavior.Restrict);


            // Conversation -> Messages (One-to-Many)
            modelBuilder.Entity<Message>()
                .HasOne(m => m.Conversation)
                .WithMany()
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);


            // User -> Sent Messages (One-to-Many)
            modelBuilder.Entity<Message>()
                .HasOne(m => m.SenderUser)
                .WithMany()
                .HasForeignKey(m => m.SenderUserId)
                .OnDelete(DeleteBehavior.Restrict);


            // User -> Password Reset Tokens (One-to-Many)
            modelBuilder.Entity<PasswordResetToken>()
                .HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // User Constraints
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<User>()
                .Property(u => u.Email)
                .HasMaxLength(256)
                .IsRequired();

            modelBuilder.Entity<User>()
                .Property(u => u.PhoneNumber)
                .HasMaxLength(20)
                .IsRequired();

            modelBuilder.Entity<User>()
                .Property(u => u.Role)
                .HasMaxLength(20)
                .IsRequired();

            // Patient Constraints
            modelBuilder.Entity<Patient>()
                .Property(p => p.FullName)
                .HasMaxLength(150)
                .IsRequired();

            modelBuilder.Entity<Patient>()
                .Property(p => p.Gender)
                .HasMaxLength(20);

            modelBuilder.Entity<Patient>()
                .Property(p => p.ProfileImageUrl)
                .HasMaxLength(500);


            // Center Constraints
            modelBuilder.Entity<Center>()
                .Property(c => c.NameArabic)
                .HasMaxLength(200)
                .IsRequired();

            modelBuilder.Entity<Center>()
                .Property(c => c.NameEnglish)
                .HasMaxLength(200);

            modelBuilder.Entity<Center>()
                .Property(c => c.DescriptionArabic)
                .HasMaxLength(1500);

            modelBuilder.Entity<Center>()
                .Property(c => c.DescriptionEnglish)
                .HasMaxLength(1500);

            modelBuilder.Entity<Center>()
                .Property(c => c.City)
                .HasMaxLength(100)
                .IsRequired();

            modelBuilder.Entity<Center>()
                .Property(c => c.Address)
                .HasMaxLength(300)
                .IsRequired();

            modelBuilder.Entity<Center>()
                .Property(c => c.ProfileImageUrl)
                .HasMaxLength(500);

            modelBuilder.Entity<Center>()
                .Property(c => c.WebsiteUrl)
                .HasMaxLength(500);

            modelBuilder.Entity<Center>()
                .Property(c => c.Status)
                .HasMaxLength(30)
                .IsRequired();

            modelBuilder.Entity<Center>()
                .Property(c => c.RejectionReason)
                .HasMaxLength(500);


            // Specialty Constraints
            modelBuilder.Entity<Specialty>()
                .Property(s => s.NameArabic)
                .HasMaxLength(150)
                .IsRequired();

            modelBuilder.Entity<Specialty>()
                .Property(s => s.NameEnglish)
                .HasMaxLength(150);

            modelBuilder.Entity<Specialty>()
                .Property(s => s.DescriptionArabic)
                .HasMaxLength(1000);

            modelBuilder.Entity<Specialty>()
                .Property(s => s.DescriptionEnglish)
                .HasMaxLength(1000);

            // Service Constraints
            modelBuilder.Entity<Service>()
                .Property(s => s.NameArabic)
                .HasMaxLength(200)
                .IsRequired();

            modelBuilder.Entity<Service>()
                .Property(s => s.NameEnglish)
                .HasMaxLength(200);

            modelBuilder.Entity<Service>()
                .Property(s => s.DescriptionArabic)
                .HasMaxLength(1500);

            modelBuilder.Entity<Service>()
                .Property(s => s.DescriptionEnglish)
                .HasMaxLength(1500);

            modelBuilder.Entity<Service>()
                .Property(s => s.Price)
                .HasPrecision(10, 2);


            // Therapist Constraints
            modelBuilder.Entity<Therapist>()
                .Property(t => t.FullName)
                .HasMaxLength(150)
                .IsRequired();

            modelBuilder.Entity<Therapist>()
                .Property(t => t.Bio)
                .HasMaxLength(1000);

            modelBuilder.Entity<Therapist>()
                .Property(t => t.ProfileImageUrl)
                .HasMaxLength(500);

            modelBuilder.Entity<Therapist>()
                .Property(t => t.Qualifications)
                .HasMaxLength(1000);

            modelBuilder.Entity<Therapist>()
                .Property(t => t.Languages)
                .HasMaxLength(300);

            // TherapistSchedule Primary Key
            modelBuilder.Entity<TherapistSchedule>()
                .HasKey(s => s.ScheduleId);

            // Appointment Constraints
            modelBuilder.Entity<Appointment>()
                .Property(a => a.PriceAtBooking)
                .HasPrecision(10, 2);

            modelBuilder.Entity<Appointment>()
                .Property(a => a.PatientNotes)
                .HasMaxLength(1000);

            modelBuilder.Entity<Appointment>()
                .Property(a => a.Status)
                .HasMaxLength(30)
                .IsRequired();

            modelBuilder.Entity<Appointment>()
                .Property(a => a.RejectionReason)
                .HasMaxLength(500);

            modelBuilder.Entity<Appointment>()
                .Property(a => a.CancellationReason)
                .HasMaxLength(500);


            // Review Constraints
            modelBuilder.Entity<Review>()
                .Property(r => r.Comment)
                .HasMaxLength(1000);

            modelBuilder.Entity<Review>()
                .HasIndex(r => r.AppointmentId)
                .IsUnique();

            // CenterWorkingHour Primary Key
            modelBuilder.Entity<CenterWorkingHour>()
                .HasKey(w => w.WorkingHourId);

            // Center Working Hour - one record per day for each center
            modelBuilder.Entity<CenterWorkingHour>()
                .HasIndex(w => new { w.CenterId, w.DayOfWeek })
                .IsUnique();


            // Conversation - one conversation between a patient and a center
            modelBuilder.Entity<Conversation>()
                .HasIndex(c => new { c.PatientId, c.CenterId })
                .IsUnique();


            // Center Image Constraints
            modelBuilder.Entity<CenterImage>()
                .Property(i => i.ImageUrl)
                .HasMaxLength(500)
                .IsRequired();


            // Center Document Constraints
            modelBuilder.Entity<CenterDocument>()
                .Property(d => d.DocumentType)
                .HasMaxLength(100)
                .IsRequired();

            modelBuilder.Entity<CenterDocument>()
                .Property(d => d.FileUrl)
                .HasMaxLength(500)
                .IsRequired();


            // Notification Constraints
            modelBuilder.Entity<Notification>()
                .Property(n => n.Type)
                .HasMaxLength(100)
                .IsRequired();

            modelBuilder.Entity<Notification>()
                .Property(n => n.Title)
                .HasMaxLength(200)
                .IsRequired();

            modelBuilder.Entity<Notification>()
                .Property(n => n.Message)
                .HasMaxLength(1000)
                .IsRequired();


            // Message Constraints
            modelBuilder.Entity<Message>()
                .Property(m => m.MessageText)
                .HasMaxLength(2000)
                .IsRequired();


            // Password Reset Token Constraints
            modelBuilder.Entity<PasswordResetToken>()
                .Property(t => t.TokenHash)
                .HasMaxLength(500)
                .IsRequired();

             
        }
    }
}
