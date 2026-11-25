using System.Globalization;
using graduatio_project.Data;
using graduatio_project.Models;
using graduatio_project.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;


// Assis-Work
namespace graduatio_project.Controllers
{
    //  اكتشاف المراكز — متاح للجميع بدون تسجيل دخول
    //  الروابط: GET /api/centers
    //           GET /api/centers/{id}
    //           GET /api/centers/{centerId}/slots
    [Route("api/centers")]
    [ApiController]
    [AllowAnonymous]
    public class CentersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        // Wires the database used by the public discovery endpoints.
        public CentersController(ApplicationDbContext context)
        {
            _context = context;
        }

        //  قائمة المراكز المعتمدة فقط — المراكز المعلقة أو المرفوضة لا تظهر أبدًا
        [HttpGet]
        public async Task<IActionResult> GetCenters([FromQuery] string? city)
        {
            IQueryable<Center> query = _context.Centers
                .AsNoTracking()
                .Include(c => c.User)
                .Where(c => c.Status == "Approved");

            if (!string.IsNullOrWhiteSpace(city))
            {
                query = query.Where(c => c.City == city);
            }

            var centers = await query
                .OrderBy(c => c.NameEnglish ?? c.NameArabic)
                .ToListAsync();

            var mapped = await CenterMapper.BuildAsync(_context, centers);

            return Ok(centers.Select(c => mapped[c.CenterId]).ToList());
        }

        //  المواعيد المتاحة لمركز ومعالج وخدمة في تاريخ معيّن
        //  القواعد نفسها التي يعتمدها الحجز: ساعات المركز، جدول المعالج،
        //  مدة الخدمة، المواعيد المحجوزة، وعدم إرجاع مواعيد في الماضي
        [HttpGet("{centerId:int}/slots")]
        public async Task<IActionResult> GetSlots(
            int centerId,
            [FromQuery] int therapistId = 0,
            [FromQuery] int serviceId = 0,
            [FromQuery] string? date = null)
        {
            var errors = new Dictionary<string, string[]>();

            if (therapistId <= 0)
            {
                errors["therapistId"] = new[] { "therapistId is required." };
            }

            if (serviceId <= 0)
            {
                errors["serviceId"] = new[] { "serviceId is required." };
            }

            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                errors["date"] = new[] { "Date must be in YYYY-MM-DD format." };
            }

            if (errors.Count > 0)
            {
                return ApiError.Validation(errors);
            }

            var (target, code, message) = await BookingRules.ResolveAsync(
                _context, centerId, therapistId, serviceId);

            if (target == null)
            {
                return ApiError.NotFound(code, message);
            }

            var slots = await BookingRules.ComputeSlotsAsync(
                _context, centerId, therapistId, serviceId, day, DateTime.Now);

            return Ok(slots);
        }
    }
}