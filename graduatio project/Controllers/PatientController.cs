using graduatio_project.Data;
using graduatio_project.DTOs.Patient;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace graduatio_project.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Patient")]
    public class PatientController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public PatientController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("profile")]
        [ProducesResponseType(typeof(PatientProfileDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetProfile()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim?.Value, out int userId))
            {
                return Unauthorized(new { message = "Invalid token." });
            }

            var profile = await _context.Patients
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .Select(p => new PatientProfileDto
                {
                    PatientId = p.PatientId,
                    FullName = p.FullName,
                    Email = p.User.Email,
                    PhoneNumber = p.User.PhoneNumber,
                    Gender = p.Gender,
                    ProfileImageUrl = p.ProfileImageUrl,
                    DateOfBirth = p.DateOfBirth,
                    City = p.City
                })
                .FirstOrDefaultAsync();

            if (profile == null)
            {
                return NotFound(new { message = "Patient not found." });
            }

            return Ok(profile);
        }

        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile(UpdatePatientProfileDto dto)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim?.Value, out int userId))
            {
                return Unauthorized(new { message = "Invalid token." });
            }

            var patient = await _context.Patients
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.UserId == userId);

            if (patient == null)
            {
                return NotFound(new { message = "Patient not found." });
            }

            bool emailExists = await _context.Users
                .AnyAsync(u => u.Email == dto.Email && u.UserId != userId);

            if (emailExists)
            {
                return BadRequest(new { message = "Email is already registered." });
            }

            patient.FullName = dto.FullName;
            patient.User.Email = dto.Email;
            patient.User.PhoneNumber = dto.PhoneNumber;
            patient.Gender = dto.Gender;
            patient.ProfileImageUrl = dto.ProfileImageUrl;
            patient.DateOfBirth = dto.DateOfBirth;
            patient.City = dto.City;

            try
            {
                // Save both linked records together. The unique email index also
                // protects against another request claiming this email concurrently.
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is SqlException sqlException &&
                (sqlException.Number == 2601 || sqlException.Number == 2627))
            {
                return BadRequest(new { message = "Email is already registered." });
            }

            return Ok(new { message = "Patient profile updated successfully." });
        }
    }
}
