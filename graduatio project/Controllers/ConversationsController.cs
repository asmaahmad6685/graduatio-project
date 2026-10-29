using System.Globalization;
using graduatio_project.Data;
using graduatio_project.DTOs.Messaging;
using graduatio_project.Models;
using graduatio_project.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace graduatio_project.Controllers
{
    //  الرسائل داخل محادثة واحدة
    //  الروابط:
    //    GET  /api/conversations/{id}/messages?since=<ISO>   للاستطلاع
    //    POST /api/conversations/{id}/messages               للإرسال
    //    POST /api/conversations/{id}/read                   لتعليم الرسائل مقروءة
    [Route("api/conversations")]
    [ApiController]
    [Authorize]
    public class ConversationsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ConversationsController(ApplicationDbContext context)
        {
            _context = context;
        }

        //  فقط طرفا المحادثة يستطيع قراءتها: المريض صاحبها أو المعالج المشارك فيها
        [HttpGet("{id:int}/messages")]
        public async Task<IActionResult> GetMessages(int id, [FromQuery] string? since)
        {
            var access = await AuthorizeConversationAsync(id);
            if (access.error != null)
            {
                return access.error;
            }

            DateTime? sinceUtc = null;
            if (!string.IsNullOrWhiteSpace(since)
                && DateTime.TryParse(since, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            {
                sinceUtc = parsed;
            }

            IQueryable<Message> query = _context.Messages
                .AsNoTracking()
                .Where(m => m.ConversationId == id);

            if (sinceUtc.HasValue)
            {
                query = query.Where(m => m.SentAt > sinceUtc.Value);
            }

            var messages = await query.OrderBy(m => m.SentAt).ToListAsync();

            var senderIds = messages.Select(m => m.SenderUserId).Distinct().ToList();
            var emails = await _context.Users.AsNoTracking()
                .Where(u => senderIds.Contains(u.UserId))
                .ToDictionaryAsync(u => u.UserId, u => u.Email);
            var therapistNames = await _context.Therapists.AsNoTracking()
                .Where(t => senderIds.Contains(t.UserId))
                .ToDictionaryAsync(t => t.UserId, t => t.FullName);

            var viewerUserId = access.userId;
            var viewerPatientName = access.conversation!.Patient?.FullName;

            var result = messages.Select(m =>
            {
                string senderName;
                if (therapistNames.TryGetValue(m.SenderUserId, out var therapistName))
                {
                    senderName = therapistName;
                }
                else if (m.SenderUserId == viewerUserId && viewerPatientName != null)
                {
                    senderName = viewerPatientName;
                }
                else
                {
                    senderName = emails.TryGetValue(m.SenderUserId, out var email)
                        ? email
                        : "Unknown";
                }

                return new
                {
                    id = m.MessageId.ToString(),
                    conversationId = m.ConversationId.ToString(),
                    senderId = m.SenderUserId.ToString(),
                    senderName,
                    text = m.MessageText,
                    attachment = m.AttachmentUrl,
                    isRead = m.IsRead,
                    sentAt = m.SentAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
                };
            }).ToList();

            return Ok(new { messages = result });
        }

        [HttpPost("{id:int}/messages")]
        public async Task<IActionResult> SendMessage(int id, SendMessageDto dto)
        {
            var access = await AuthorizeConversationAsync(id);
            if (access.error != null)
            {
                return access.error;
            }

            if (!CurrentUser.TryGetUserId(User, out var userId))
            {
                return ApiError.Unauthorized("unauthenticated", "Authentication is required.");
            }

            var message = new Message
            {
                ConversationId = id,
                SenderUserId = userId,
                MessageText = dto.Text.Trim(),
                AttachmentUrl = string.IsNullOrWhiteSpace(dto.Attachment) ? null : dto.Attachment,
                IsRead = false,
                SentAt = DateTime.UtcNow
            };

            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                id = message.MessageId.ToString(),
                conversationId = message.ConversationId.ToString(),
                senderId = message.SenderUserId.ToString(),
                text = message.MessageText,
                attachment = message.AttachmentUrl,
                isRead = message.IsRead,
                sentAt = message.SentAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
            });
        }

        //  نعلّم الرسائل المحددة مقروءة — نتجاهل أي معرّف لا يخص هذه المحادثة
        [HttpPost("{id:int}/read")]
        public async Task<IActionResult> MarkRead(int id, MarkMessagesReadDto dto)
        {
            var access = await AuthorizeConversationAsync(id);
            if (access.error != null)
            {
                return access.error;
            }

            var messageIds = dto.MessageIds.Distinct().ToList();

            var messages = await _context.Messages
                .Where(m => m.ConversationId == id && messageIds.Contains(m.MessageId))
                .ToListAsync();

            foreach (var message in messages)
            {
                message.IsRead = true;
            }

            await _context.SaveChangesAsync();

            return NoContent();
        }

        //  نتحقق أن المستخدم الحالي طرفًا في المحادثة
        private async Task<(Conversation? conversation, int userId, IActionResult? error)>
            AuthorizeConversationAsync(int conversationId)
        {
            if (!CurrentUser.TryGetUserId(User, out var userId))
            {
                return (null, 0, ApiError.Unauthorized(
                    "unauthenticated", "Authentication is required."));
            }

            var conversation = await _context.Conversations.AsNoTracking()
                .Include(c => c.Patient)
                .FirstOrDefaultAsync(c => c.ConversationId == conversationId);

            if (conversation == null)
            {
                return (null, userId, ApiError.NotFound("not_found", "Conversation not found."));
            }

            var isPatient = CurrentUser.IsRole(User, "Patient")
                && conversation.Patient?.UserId == userId;
            var isTherapist = CurrentUser.IsRole(User, "Therapist")
                && conversation.TherapistId != 0
                && await _context.Therapists.AsNoTracking()
                    .AnyAsync(t => t.TherapistId == conversation.TherapistId
                        && t.UserId == userId);

            //  المرضى لا يطّلعون على محادثات غيرهم مهما كان المعرّف
            if (!isPatient && !isTherapist)
            {
                return (null, userId, ApiError.NotFound("not_found", "Conversation not found."));
            }

            return (conversation, userId, null);
        }
    }
}