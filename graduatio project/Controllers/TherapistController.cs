using graduatio_project.Data;
using graduatio_project.DTOs.Therapist;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace graduatio_project.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Therapist")]

    public class TherapistController : ControllerBase
    {
        //  نستخدم DbContext للوصول إلى قاعدة البيانات
        private readonly ApplicationDbContext _context;

        //  Constructor يستقبل DbContext حتى نستخدمه داخل الـ APIs
        public TherapistController(ApplicationDbContext context)
        {
            _context = context;
        }

        //  هذا الـ API يرجع بيانات الـ Therapist المسجل دخول حالياً
        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            // نجيب UserId من الـ JWT Token
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);

            //  إذا لم نجد UserId داخل الـ Token نوقف العملية
            if (userIdClaim == null)
            {
                return Unauthorized(new
                {
                    message = "Invalid token."
                });
            }

            //  نحول UserId من نص إلى رقم
            int userId = int.Parse(userIdClaim.Value);

            //  نجيب بيانات الـ Therapist المرتبطة بهذا الـ User
            // ونجيب معها التخصص والمركز
            var therapist = await _context.Therapists
                .Include(t => t.Specialty)
                .Include(t => t.Center)
                .FirstOrDefaultAsync(t => t.UserId == userId);

            //  إذا لم نجد بيانات الـ Therapist نرجع Not Found
            if (therapist == null)
            {
                return NotFound(new
                {
                    message = "Therapist not found."
                });
            }

            //  نرجع بيانات الـ Profile للـ Frontend
            return Ok(new
            {
                therapistId = therapist.TherapistId,
                fullName = therapist.FullName,
                bio = therapist.Bio,
                profileImageUrl = therapist.ProfileImageUrl,
                yearsOfExperience = therapist.YearsOfExperience,
                qualifications = therapist.Qualifications,
                languages = therapist.Languages,
                isAvailable = therapist.IsAvailable,
                isActive = therapist.IsActive,

                specialty = new
                {
                    specialtyId = therapist.Specialty.SpecialtyId,
                    nameArabic = therapist.Specialty.NameArabic,
                    nameEnglish = therapist.Specialty.NameEnglish
                },

                center = new
                {
                    centerId = therapist.Center.CenterId,
                    nameArabic = therapist.Center.NameArabic,
                    nameEnglish = therapist.Center.NameEnglish
                }
            });
        }
        //  هذا الـ API يسمح للـ Therapist بتعديل بيانات الـ Profile الخاصة فيه
        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile(UpdateTherapistProfileDto dto)
        {
            //  نجيب UserId من الـ JWT Token
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);

            //  إذا لم نجد UserId داخل الـ Token نوقف العملية
            if (userIdClaim == null)
            {
                return Unauthorized(new
                {
                    message = "Invalid token."
                });
            }

            //  نحول UserId من نص إلى رقم
            int userId = int.Parse(userIdClaim.Value);

            // نبحث عن الـ Therapist المرتبط بالمستخدم الحالي
            var therapist = await _context.Therapists
                .FirstOrDefaultAsync(t => t.UserId == userId);

            // : إذا لم نجد الـ Therapist نرجع Not Found
            if (therapist == null)
            {
                return NotFound(new
                {
                    message = "Therapist not found."
                });
            }

            // نتأكد أن الـ Specialty الجديدة موجودة فعلاً
            bool specialtyExists = await _context.Specialties
                .AnyAsync(s => s.SpecialtyId == dto.SpecialtyId);

            if (!specialtyExists)
            {
                return BadRequest(new
                {
                    message = "Invalid specialty."
                });
            }

            //  نحدث بيانات الـ Therapist بالقيم الجديدة
            therapist.FullName = dto.FullName;
            therapist.SpecialtyId = dto.SpecialtyId;
            therapist.Bio = dto.Bio;
            therapist.ProfileImageUrl = dto.ProfileImageUrl;
            therapist.YearsOfExperience = dto.YearsOfExperience;
            therapist.Qualifications = dto.Qualifications;
            therapist.Languages = dto.Languages;
            therapist.IsAvailable = dto.IsAvailable;

            //  نحفظ التعديلات في قاعدة البيانات
            await _context.SaveChangesAsync();

            //  نرجع رسالة نجاح للـ Frontend
            return Ok(new
            {
                message = "Therapist profile updated successfully."
            });

        }
    }
}
