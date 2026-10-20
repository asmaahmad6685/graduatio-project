using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using graduatio_project.Data;
using graduatio_project.DTOs.Therapist;
using graduatio_project.Models;
using graduatio_project.Services;
using Microsoft.AspNetCore.Authorization;

using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace graduatio_project.Controllers
{
    [Route("api/[controller]")]
    [ApiController]

    [Authorize(Roles = "Center")]
    public class CenterController : ControllerBase
    {
        // نستخدم DbContext للوصول إلى قاعدة البيانات
        private readonly ApplicationDbContext _context;

        //  نستخدم PasswordService لعمل Hash لباسورد المعالج
        private readonly PasswordService _passwordService;

        //  Constructor يستقبل الخدمات التي يحتاجها الـ Controller
        public CenterController(
            ApplicationDbContext context,
            PasswordService passwordService)
        {
            _context = context;
            _passwordService = passwordService;
        }
        //  هذا الـ API يسمح للمركز بإضافة Therapist جديد
        // ويعمل له User Account حتى يقدر يعمل Login بنفس الإيميل والباسورد
        [HttpPost("therapists")]
        public async Task<IActionResult> CreateTherapist(CreateTherapistDto dto)
        {
            //  نجيب UserId الخاص بالمركز من الـ JWT Token
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
            int centerUserId = int.Parse(userIdClaim.Value);

            //  نجيب بيانات المركز المرتبطة بحساب الـ User
            var center = await _context.Centers
                .FirstOrDefaultAsync(c => c.UserId == centerUserId);

            //  إذا لم نجد المركز نوقف العملية
            if (center == null)
            {
                return NotFound(new
                {
                    message = "Center not found."
                });
            }

            //  نتأكد أن الإيميل غير مستخدم مسبقاً
            bool emailExists = await _context.Users
                .AnyAsync(u => u.Email == dto.Email);

            if (emailExists)
            {
                return BadRequest(new
                {
                    message = "Email is already registered."
                });
            }

            //  نتأكد أن الـ Specialty موجودة
            bool specialtyExists = await _context.Specialties
                .AnyAsync(s => s.SpecialtyId == dto.SpecialtyId);

            if (!specialtyExists)
            {
                return BadRequest(new
                {
                    message = "Invalid specialty."
                });
            }

            //  ننشئ User Account للـ Therapist
            var therapistUser = new User
            {
                Email = dto.Email,
                PasswordHash = _passwordService.HashPassword(dto.Password),
                PhoneNumber = string.Empty,
                Role = "Therapist",
                IsActive = true
            };

            _context.Users.Add(therapistUser);

            //  ننشئ Therapist Profile ونربطه بحساب الـ User وبالمركز الحالي
            var therapist = new Therapist
            {
                User = therapistUser,
                CenterId = center.CenterId,
                SpecialtyId = dto.SpecialtyId,
                FullName = dto.FullName,
                Bio = dto.Bio,
                ProfileImageUrl = dto.ProfileImageUrl,
                YearsOfExperience = dto.YearsOfExperience,
                Qualifications = dto.Qualifications,
                Languages = dto.Languages,
                IsAvailable = true,
                IsActive = true
            };

            _context.Therapists.Add(therapist);

            // Save the User and Therapist together in EF Core's automatic transaction.
            await _context.SaveChangesAsync();

            //  نرجع Response للـ Frontend بعد نجاح الإضافة
            return Ok(new
            {
                message = "Therapist created successfully.",
                therapistId = therapist.TherapistId,
                userId = therapistUser.UserId,
                email = therapistUser.Email,
                role = therapistUser.Role
            });



        }
    }
}
