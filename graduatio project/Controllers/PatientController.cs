using graduatio_project.Data;
using graduatio_project.DTOs.Patient;
using graduatio_project.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
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

        [HttpGet("saved-centers")]
        [ProducesResponseType(typeof(List<SavedCenterDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSavedCenters()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim?.Value, out int userId))
            {
                return Unauthorized(new { message = "Invalid token." });
            }

            var patientId = await GetPatientIdAsync(userId);
            if (patientId == null)
            {
                return NotFound(new { message = "Patient not found." });
            }

            // Keep existing favorites visible even if a center's status changes.
            var centers = await _context.PatientSavedCenters
                .AsNoTracking()
                .Where(sc => sc.PatientId == patientId.Value)
                .OrderByDescending(sc => sc.SavedAt)
                .ThenBy(sc => sc.CenterId)
                .Select(sc => new SavedCenterDto
                {
                    CenterId = sc.CenterId,
                    NameArabic = sc.Center.NameArabic,
                    NameEnglish = sc.Center.NameEnglish,
                    DescriptionArabic = sc.Center.DescriptionArabic,
                    DescriptionEnglish = sc.Center.DescriptionEnglish,
                    City = sc.Center.City,
                    Address = sc.Center.Address,
                    ProfileImageUrl = sc.Center.ProfileImageUrl,
                    Latitude = sc.Center.Latitude,
                    Longitude = sc.Center.Longitude,
                    Status = sc.Center.Status,
                    IsSaved = true,
                    SavedAt = sc.SavedAt
                })
                .ToListAsync();

            return Ok(centers);
        }

        [HttpPut("saved-centers/{centerId:int}")]
        public async Task<IActionResult> SaveCenter(int centerId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim?.Value, out int userId))
            {
                return Unauthorized(new { message = "Invalid token." });
            }

            var patientId = await GetPatientIdAsync(userId);
            if (patientId == null)
            {
                return NotFound(new { message = "Patient not found." });
            }

            // Hold center/account read locks until commit so eligibility cannot
            // change between validation and insertion, including under RCSI.
            await using var transaction = await _context.Database
                .BeginTransactionAsync(IsolationLevel.Serializable);

            var center = await _context.Centers
                .AsNoTracking()
                .Where(c => c.CenterId == centerId)
                .Select(c => new { c.Status, c.User.IsActive })
                .FirstOrDefaultAsync();

            if (center == null)
            {
                return NotFound(new { message = "Center not found." });
            }

            // Lock the composite key (or its gap if absent). UPDLOCK avoids
            // competing saves both taking shared locks then attempting inserts.
            // DELETE must also wait for this lock, preventing the former
            // duplicate-key/recheck race. Values are SQL parameters.
            var existingSave = await _context.PatientSavedCenters
                .FromSqlInterpolated($@"
                    SELECT [PatientId], [CenterId], [SavedAt]
                    FROM [PatientSavedCenters] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [PatientId] = {patientId.Value} AND [CenterId] = {centerId}")
                .AsNoTracking()
                .FirstOrDefaultAsync();

            // A repeated save remains successful even after a status change.
            if (existingSave != null)
            {
                await transaction.CommitAsync();
                return Ok(new { message = "Center saved successfully.", centerId, isSaved = true });
            }

            if (center.Status != "Approved" || !center.IsActive)
            {
                return BadRequest(new { message = "Only approved, active centers can be saved." });
            }

            var savedCenter = new PatientSavedCenter
            {
                PatientId = patientId.Value,
                CenterId = centerId
            };

            _context.PatientSavedCenters.Add(savedCenter);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new { message = "Center saved successfully.", centerId, isSaved = true });
        }

        [HttpDelete("saved-centers/{centerId:int}")]
        public async Task<IActionResult> RemoveSavedCenter(int centerId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim?.Value, out int userId))
            {
                return Unauthorized(new { message = "Invalid token." });
            }

            var patientId = await GetPatientIdAsync(userId);
            if (patientId == null)
            {
                return NotFound(new { message = "Patient not found." });
            }

            bool centerExists = await _context.Centers.AnyAsync(c => c.CenterId == centerId);
            if (!centerExists)
            {
                return NotFound(new { message = "Center not found." });
            }

            // Deleting zero rows is also success, including concurrent removals.
            await _context.PatientSavedCenters
                .Where(sc => sc.PatientId == patientId.Value && sc.CenterId == centerId)
                .ExecuteDeleteAsync();

            return Ok(new { message = "Center removed from saved centers.", centerId, isSaved = false });
        }

        private Task<int?> GetPatientIdAsync(int userId)
        {
            return _context.Patients
                .Where(p => p.UserId == userId)
                .Select(p => (int?)p.PatientId)
                .FirstOrDefaultAsync();
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
