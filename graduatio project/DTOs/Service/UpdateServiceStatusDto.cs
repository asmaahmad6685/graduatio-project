using System.ComponentModel.DataAnnotations;

namespace graduatio_project.DTOs.Service
{
    public class UpdateServiceStatusDto
    {
        [Required]
        public bool? IsActive { get; set; }
    }
}
