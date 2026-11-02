using System.Globalization;
using graduatio_project.Hubs;
using Microsoft.AspNetCore.SignalR;
using graduatio_project.Data;
using graduatio_project.DTOs.Messaging;
using graduatio_project.Models;
using graduatio_project.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
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
        private readonly IHubContext<MessagingHub> _hub;

        public ConversationsController(ApplicationDbContext context,
            IHubContext<MessagingHub> hub)
        {
            _context = context;
            _hub = hub;
        }

        //  فتح محادثة: المريض يفتح محادثة مع معالج — يُنشأ سطر جديد فقط إذا لم يكن موجودًا
        //  الفهرس الفريد (PatientId, TherapistId) هو الضمان ضد الطلبات المتزامنة
        [HttpPost]
        [Authorize(Roles = "Patient")]
        public async Task<IActionResult> OpenConversation(OpenConversationDto dto, CancellationToken ct)
        {
            if (!CurrentUser.TryGetUserId(User, out var userId))
            {
                return ApiError.Unauthorized("unauthenticated", "Authentication is required.");
            }

            var patient = await _context.Patients.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId, ct);
            if (patient == null)
            {
                return ApiError.NotFound("not_found", "Patient profile not found.");
            }

            var therapist = await _context.Therapists.AsNoTracking()
                .FirstOrDefaultAsync(t => t.TherapistId == dto.TherapistId, ct);
            if (therapist == null)
            {
                return ApiError.NotFound("not_found", "Physiotherapist not found.");
            }

            var conversation = await _context.Conversations.AsNoTracking()
                .FirstOrDefaultAsync(c => c.PatientId == patient.PatientId
                    && c.TherapistId == therapist.TherapistId, ct);

            var created = false;
            if (conversation == null)
            {
                conversation = new Conversation
                {
                    PatientId = patient.PatientId,
                    TherapistId = therapist.TherapistId,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Conversations.Add(conversation);
                created = true;
                try
                {
                    await _context.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
                {
                    //  طلب متزامن أنشأها أولًا — نعيد قراءة السطر الفائز
                    conversation = await _context.Conversations.AsNoTracking()
                        .FirstOrDefaultAsync(c => c.PatientId == patient.PatientId
                            && c.TherapistId == therapist.TherapistId, ct);
                    if (conversation == null) throw;
                }
            }

            //  دفع فوري للمعالج: المحادثة الجديدة تظهر في قائمته دون انتظار البولي
            if (created)
            {
                await NotifyConversationAsync(conversation, "opened");
            }

            return Ok(new
            {
                id = conversation.ConversationId.ToString(),
                patientId = conversation.PatientId.ToString(),
                therapistId = conversation.TherapistId.ToString()
            });
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

            //  ThenBy يمنع ترتيبًا عشوائيًا للرسائل المُرسلة في نفس الثانية
            var messages = await query
                .OrderBy(m => m.SentAt)
                .ThenBy(m => m.MessageId)
                .ToListAsync();

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

            var text = dto.Text?.Trim() ?? string.Empty;
            var attachment = string.IsNullOrWhiteSpace(dto.Attachment) ? null : dto.Attachment.Trim();

            //  الرسالة الفارغة تمامًا مرفوضة: نص أو مرفق على الأقل
            if (text.Length == 0 && attachment == null)
            {
                return ApiError.Validation(new Dictionary<string, string[]>
                {
                    ["Text"] = new[] { "A message needs text or an attachment." }
                });
            }

            var message = new Message
            {
                ConversationId = id,
                SenderUserId = userId,
                MessageText = text,
                AttachmentUrl = attachment,
                IsRead = false,
                SentAt = DateTime.UtcNow
            };

            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            //  الحدث يُدفع للطرف الآخر فورًا؛ البث الفاشل لا يُبطل الرسالة المحفوظة
            await NotifyConversationAsync(access.conversation!, "message");

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

            //  إيصال القراءة يصل المرسل في نفس اللحظة
            await NotifyConversationAsync(access.conversation!, "read");

            return NoContent();
        }

        //  نتحقق أن المستخدم الحالي طرفًا في المحادثة
        //  دفع فوري إلى كل جلسات طرفي المحادثة عبر SignalR:
        //  البث خطوة منفصلة عن الحفظ، فشله يُسجَّل فقط ولا يُسقط استجابة ناجحة،
        //  والبولي الصامدة (20 ثانية) يبقى شبكة الأمان لكل حدث ضائع
        private async Task NotifyConversationAsync(Conversation conversation, string type)
        {
            try
            {
                var userIds = new List<int>();

                var patientUserId = conversation.Patient?.UserId ?? 0;
                if (patientUserId == 0 && conversation.PatientId != 0)
                {
                    patientUserId = await _context.Patients.AsNoTracking()
                        .Where(p => p.PatientId == conversation.PatientId)
                        .Select(p => p.UserId)
                        .FirstOrDefaultAsync();
                }
                if (patientUserId != 0)
                {
                    userIds.Add(patientUserId);
                }

                if (conversation.TherapistId != 0)
                {
                    var therapistUserId = await _context.Therapists.AsNoTracking()
                        .Where(t => t.TherapistId == conversation.TherapistId)
                        .Select(t => t.UserId)
                        .FirstOrDefaultAsync();
                    if (therapistUserId != 0)
                    {
                        userIds.Add(therapistUserId);
                    }
                }

                var targets = userIds.Distinct().Select(id => id.ToString()).ToArray();
                if (targets.Length == 0)
                {
                    return;
                }

                await _hub.Clients.Users(targets).SendAsync("workspaceChanged", new
                {
                    type,
                    conversationId = conversation.ConversationId.ToString()
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Messaging] push failed for conversation {conversation.ConversationId}: {ex.Message}");
            }
        }

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