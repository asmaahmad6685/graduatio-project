using System.ComponentModel.DataAnnotations;

namespace graduatio_project.DTOs.Patient
{
    //  تقييم جديد يكتبه المريض لمركز زاره
    public class CreatePatientReviewDto
    {
        [Range(1, int.MaxValue)]
        public int CenterId { get; set; }

        [Range(1, 5)]
        public int Rating { get; set; }

        [MaxLength(1000)]
        public string? Comment { get; set; }
    }
}
