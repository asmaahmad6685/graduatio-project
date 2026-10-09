namespace graduatio_project.Models
{
    public class Therapist
    {
  
            public int TherapistId { get; set; }
        //  هذا الحقل يربط المعالج بحساب User حتى يقدر يعمل Login
        public int UserId { get; set; }

        //  Navigation Property تربط Therapist مع User
        public User User { get; set; } = null!;

        public int CenterId { get; set; }

            public int SpecialtyId { get; set; }

            public string FullName { get; set; } = string.Empty;

            public string? Bio { get; set; }

            public string? ProfileImageUrl { get; set; }

            public int? YearsOfExperience { get; set; }

            public string? Qualifications { get; set; }

            public string? Languages { get; set; }

            public bool IsAvailable { get; set; } = true;

            public bool IsActive { get; set; } = true;

            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

            public Center Center { get; set; } = null!;

            public Specialty Specialty { get; set; } = null!;

    
    }
    }

