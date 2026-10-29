using System.Globalization;
using graduatio_project.Data;
using graduatio_project.DTOs.Admin;
using graduatio_project.Models;
using graduatio_project.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace graduatio_project.Controllers
{
    //  مراجعة طلبات افتتاح المراكز
    //  الروابط:
    //    GET  /api/admin/center-applications?status=pending|approved|rejected
    //    GET  /api/admin/center-applications/{id}
    //    POST /api/admin/center-applications/{id}/review {decision, reason}
    [Route("api/admin/center-applications")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public AdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetApplications([FromQuery] string? status)
        {
            IQueryable<Center> query = _context.Centers
                .AsNoTracking()
                .Include(c => c.User);

            if (!string.IsNullOrWhiteSpace(status))
            {
                var normalized = status.Trim();
                query = query.Where(c => c.Status == normalized);
            }

            var centers = await query.OrderByDescending(c => c.CreatedAt).ToListAsync();

            return Ok(centers.Select(ApplicationDto).ToList());
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetApplication(int id)
        {
            var center = await _context.Centers
                .AsNoTracking()
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.CenterId == id);

            if (center == null)
            {
                return ApiError.NotFound("not_found", "Center application not found.");
            }

            return Ok(ApplicationDto(center));
        }

        [HttpPost("{id:int}/review")]
        public async Task<IActionResult> Review(int id, ReviewCenterApplicationDto dto)
        {
            var center = await _context.Centers
                .FirstOrDefaultAsync(c => c.CenterId == id);

            if (center == null)
            {
                return ApiError.NotFound("not_found", "Center application not found.");
            }

            var decision = dto.Decision.Trim().ToLowerInvariant();

            //  الرفض لازم يحمل سببًا حتى يظهر لصاحب الطلب في شاشته
            if (decision == "rejected" && string.IsNullOrWhiteSpace(dto.Reason))
            {
                return ApiError.Validation(new Dictionary<string, string[]>
                {
                    ["Reason"] = new[] { "A reason is required when rejecting an application." }
                }, "A reason is required when rejecting an application.");
            }

            var admin = await _context.Admins.AsNoTracking()
                .FirstOrDefaultAsync(a => a.UserId == GetCurrentUserId());

            if (admin == null)
            {
                return ApiError.NotFound("not_found", "Admin profile not found.");
            }

            center.Status = decision == "approved" ? "Approved" : "Rejected";
            center.ApprovedByAdminId = admin.AdminId;
            center.ReviewedAt = DateTime.UtcNow;
            center.UpdatedAt = DateTime.UtcNow;
            center.RejectionReason = decision == "approved" ? null : dto.Reason?.Trim();

            if (decision == "approved")
            {
                center.ApprovedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            //  نعيد نفس شكل الطلب حتى تحدّث الواجهة صفّها دون إعادة جلب
            var tracked = await _context.Centers
                .AsNoTracking()
                .Include(c => c.User)
                .FirstAsync(c => c.CenterId == id);

            return Ok(ApplicationDto(tracked));
        }

        private int GetCurrentUserId()
            => CurrentUser.TryGetUserId(User, out var userId) ? userId : 0;

        private static object ApplicationDto(Center center) => new
        {
            id = center.CenterId.ToString(),
            name = CenterMapper.Pick(center.NameEnglish, center.NameArabic),
            email = center.User?.Email,
            phone = center.User?.PhoneNumber,
            location = new
            {
                address = center.Address,
                coordinates = new
                {
                    latitude = center.Latitude,
                    longitude = center.Longitude
                },
                confirmed = center.LocationConfirmed
            },
            approvalStatus = center.Status.ToLowerInvariant(),
            submittedAt = center.CreatedAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            status = center.Status.ToLowerInvariant(),
            reviewedAt = center.ReviewedAt?.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            reviewReason = center.RejectionReason
        };
    }
}