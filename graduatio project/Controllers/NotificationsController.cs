using graduatio_project.Data;
using graduatio_project.DTOs.Messaging;
using graduatio_project.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace graduatio_project.Controllers
{
    //  الإشعارات: التعليم كمقروء
    //  الرابط: POST /api/notifications/read { id }
    [Route("api/notifications")]
    [ApiController]
    [Authorize]
    public class NotificationsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public NotificationsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost("read")]
        public async Task<IActionResult> MarkRead(MarkNotificationReadDto dto)
        {
            if (!CurrentUser.TryGetUserId(User, out var userId))
            {
                return ApiError.Unauthorized("unauthenticated", "Authentication is required.");
            }

            //  نبحث بالإيميل + المعرّف معاً حتى لا يعلّم أحد إشعار غيره
            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.NotificationId == dto.Id
                    && n.UserId == userId);

            //  إشعار غير موجود أو غير مملوك: 404 بلا معلومات عن الآخرين
            if (notification == null)
            {
                return ApiError.NotFound("not_found", "Notification not found.");
            }

            notification.IsRead = true;
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}