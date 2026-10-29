using System.Linq.Expressions;
using System.Security.Claims;
using graduatio_project.Data;
using graduatio_project.DTOs.Service;
using graduatio_project.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace graduatio_project.Controllers
{
    [ApiController]
    [Route("api/center/services")]
    [Authorize(Roles = "Center")]
    public class ServicesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ServicesController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> CreateService(CreateServiceDto dto)
        {
            var (center, error) = await GetEligibleCenterAsync();
            if (error != null) return error;

            if (!await IsActiveSpecialtyAsync(dto.SpecialtyId!.Value))
                return BadRequest(new { message = "Specialty must exist and be active." });

            var service = new Service { CenterId = center!.CenterId, IsActive = true };
            ApplyFields(service, dto);
            _context.Services.Add(service);
            await _context.SaveChangesAsync();

            var response = await ServiceResponses()
                .SingleAsync(s => s.ServiceId == service.ServiceId && s.CenterId == center.CenterId);
            return CreatedAtAction(nameof(GetOwnService), new { serviceId = service.ServiceId }, response);
        }

        [HttpGet]
        public async Task<IActionResult> GetOwnServices([FromQuery] bool? isActive)
        {
            var (center, error) = await GetEligibleCenterAsync();
            if (error != null) return error;

            var query = ServiceResponses().Where(s => s.CenterId == center!.CenterId);
            if (isActive.HasValue) query = query.Where(s => s.IsActive == isActive.Value);
            return Ok(await query.OrderBy(s => s.ServiceId).ToListAsync());
        }

        [HttpGet("{serviceId:int}")]
        public async Task<IActionResult> GetOwnService(int serviceId)
        {
            var (center, error) = await GetEligibleCenterAsync();
            if (error != null) return error;
            if (serviceId <= 0) return BadRequest(new { message = "Invalid service ID." });

            var service = await ServiceResponses().FirstOrDefaultAsync(
                s => s.ServiceId == serviceId && s.CenterId == center!.CenterId);
            if (service == null) return NotFound(new { message = "Service not found." });
            return Ok(service);
        }

        [HttpPut("{serviceId:int}")]
        public async Task<IActionResult> UpdateService(int serviceId, UpdateServiceDto dto)
        {
            var (center, error) = await GetEligibleCenterAsync();
            if (error != null) return error;
            if (serviceId <= 0) return BadRequest(new { message = "Invalid service ID." });

            var service = await _context.Services.FirstOrDefaultAsync(
                s => s.ServiceId == serviceId && s.CenterId == center!.CenterId);
            if (service == null) return NotFound(new { message = "Service not found." });

            if (service.SpecialtyId != dto.SpecialtyId!.Value)
            {
                if (await _context.TherapistServices.AnyAsync(ts => ts.ServiceId == service.ServiceId))
                    return Conflict(new { message = "Cannot change specialty while the service has therapist assignments." });

                if (!await IsActiveSpecialtyAsync(dto.SpecialtyId.Value))
                    return BadRequest(new { message = "Specialty must exist and be active." });
            }

            ApplyFields(service, dto);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Service updated successfully." });
        }

        [HttpPatch("{serviceId:int}/status")]
        public async Task<IActionResult> UpdateServiceStatus(int serviceId, UpdateServiceStatusDto dto)
        {
            var (center, error) = await GetEligibleCenterAsync();
            if (error != null) return error;
            if (serviceId <= 0) return BadRequest(new { message = "Invalid service ID." });

            var service = await _context.Services.FirstOrDefaultAsync(
                s => s.ServiceId == serviceId && s.CenterId == center!.CenterId);
            if (service == null) return NotFound(new { message = "Service not found." });

            if (dto.IsActive!.Value && !await IsActiveSpecialtyAsync(service.SpecialtyId))
                return BadRequest(new { message = "Specialty must exist and be active to activate this service." });

            service.IsActive = dto.IsActive.Value;
            await _context.SaveChangesAsync();
            return Ok(new { message = "Service status updated successfully.", isActive = service.IsActive });
        }

        [AllowAnonymous]
        [HttpGet("/api/centers/{centerId:int}/services")]
        public async Task<IActionResult> GetPublicServices(int centerId)
        {
            if (centerId <= 0) return BadRequest(new { message = "Invalid center ID." });
            if (!await IsPublicCenterAsync(centerId))
                return NotFound(new { message = "Center not found." });

            return Ok(await PublicServices(centerId).Select(ToResponse)
                .OrderBy(s => s.ServiceId).ToListAsync());
        }

        [AllowAnonymous]
        [HttpGet("/api/centers/{centerId:int}/services/{serviceId:int}")]
        public async Task<IActionResult> GetPublicService(int centerId, int serviceId)
        {
            if (centerId <= 0 || serviceId <= 0)
                return BadRequest(new { message = "Invalid center or service ID." });
            if (!await IsPublicCenterAsync(centerId))
                return NotFound(new { message = "Center not found." });

            var service = await PublicServices(centerId).Where(s => s.ServiceId == serviceId)
                .Select(ToResponse).FirstOrDefaultAsync();
            if (service == null) return NotFound(new { message = "Service not found." });
            return Ok(service);
        }

        private async Task<(Center? Center, IActionResult? Error)> GetEligibleCenterAsync()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
                return (null, Unauthorized(new { message = "Invalid token." }));

            var center = await _context.Centers.AsNoTracking().Include(c => c.User)
                .FirstOrDefaultAsync(c => c.UserId == userId);
            if (center == null)
                return (null, NotFound(new { message = "Center not found." }));
            if (center.Status != "Approved" || center.User == null || !center.User.IsActive)
                return (null, StatusCode(403, new { message = "Your center must be approved and active to manage services." }));
            return (center, null);
        }

        private Task<bool> IsActiveSpecialtyAsync(int specialtyId) =>
            _context.Specialties.AnyAsync(s => s.SpecialtyId == specialtyId && s.IsActive);

        private Task<bool> IsPublicCenterAsync(int centerId) =>
            _context.Centers.AnyAsync(c => c.CenterId == centerId && c.Status == "Approved"
                && c.User.IsActive);

        private IQueryable<Service> PublicServices(int centerId) =>
            _context.Services.AsNoTracking().Where(s => s.CenterId == centerId && s.IsActive
                && s.Specialty.IsActive && s.Center.Status == "Approved" && s.Center.User.IsActive);

        private IQueryable<ServiceResponseDto> ServiceResponses() =>
            _context.Services.AsNoTracking().Select(ToResponse);

        private static readonly Expression<Func<Service, ServiceResponseDto>> ToResponse = s =>
            new ServiceResponseDto
            {
                ServiceId = s.ServiceId,
                CenterId = s.CenterId,
                SpecialtyId = s.SpecialtyId,
                SpecialtyNameArabic = s.Specialty.NameArabic,
                SpecialtyNameEnglish = s.Specialty.NameEnglish,
                NameArabic = s.NameArabic,
                NameEnglish = s.NameEnglish,
                DescriptionArabic = s.DescriptionArabic,
                DescriptionEnglish = s.DescriptionEnglish,
                Price = s.Price,
                DurationMinutes = s.DurationMinutes,
                IsActive = s.IsActive,
                CreatedAt = s.CreatedAt
            };

        private static void ApplyFields(Service service, CreateServiceDto dto)
        {
            service.NameArabic = dto.NameArabic.Trim();
            service.NameEnglish = NormalizeOptional(dto.NameEnglish);
            service.DescriptionArabic = NormalizeOptional(dto.DescriptionArabic);
            service.DescriptionEnglish = NormalizeOptional(dto.DescriptionEnglish);
            service.Price = dto.Price!.Value;
            service.DurationMinutes = dto.DurationMinutes!.Value;
            service.SpecialtyId = dto.SpecialtyId!.Value;
        }

        private static string? NormalizeOptional(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
