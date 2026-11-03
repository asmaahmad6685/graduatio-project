using graduatio_project.Data;
using graduatio_project.Hubs;
using graduatio_project.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace graduatio_project.Services
{
    //  إنشاء إشعارات داخل التطبيق لكل الأدوار عند الأحداث الحقيقية
    //  (رسالة، موعد، اعتماد مركز، تحديث ملف معالج...) مع دفع فوري عبر SignalR
    //  حتى يظهر الجرس لحظيًا. أي فشل يُسجَّل فقط ولا يُبطل الإجراء الأصلي.
    public class NotificationService(
        ApplicationDbContext context,
        IHubContext<MessagingHub> hub)
    {
        //  إشعار واحد لمستخدم واحد
        public Task NotifyAsync(
            int userId,
            string type,
            string title,
            string message,
            int? relatedEntityId = null,
            bool onlyWhenUnreadExists = false)
            => NotifyAsync(new[] { userId }, type, title, message, relatedEntityId, onlyWhenUnreadExists);

        //  إشعار لعدة مستخدمين في حفظة واحدة وبث واحد
        public async Task NotifyAsync(
            IReadOnlyCollection<int> userIds,
            string type,
            string title,
            string message,
            int? relatedEntityId = null,
            bool onlyWhenUnreadExists = false)
        {
            var targets = userIds.Where(id => id > 0).Distinct().ToList();
            if (targets.Count == 0)
            {
                return;
            }

            try
            {
                if (onlyWhenUnreadExists)
                {
                    //  رسالة غير مقروءة واحدة تكفي: لا نكدّر الجرس برسائل متتالية
                    var blocked = (await context.Notifications.AsNoTracking()
                        .Where(n => targets.Contains(n.UserId)
                            && n.Type == type
                            && n.RelatedEntityId == relatedEntityId
                            && !n.IsRead)
                        .Select(n => n.UserId)
                        .Distinct()
                        .ToListAsync()).ToHashSet();

                    targets = targets.Where(id => !blocked.Contains(id)).ToList();
                    if (targets.Count == 0)
                    {
                        return;
                    }
                }

                foreach (var userId in targets)
                {
                    context.Notifications.Add(new Notification
                    {
                        UserId = userId,
                        Type = type,
                        Title = title,
                        Message = message,
                        RelatedEntityId = relatedEntityId
                    });
                }

                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Notifications] create failed ({type}): {ex.Message}");
                return;
            }

            await PushAsync(targets);
        }

        //  إشعارات الأدمن: كل حسابات الأدمن المسجّلة
        public async Task NotifyAdminsAsync(string type, string title, string message, int? relatedEntityId = null)
        {
            var adminIds = await context.Users.AsNoTracking()
                .Where(u => u.Role == "Admin")
                .Select(u => u.UserId)
                .ToListAsync();

            await NotifyAsync(adminIds, type, title, message, relatedEntityId);
        }

        //  بث فوري لكل الجلسات المفتوحة لهؤلاء المستخدمين
        private async Task PushAsync(IReadOnlyCollection<int> userIds)
        {
            try
            {
                var targets = userIds
                    .Where(id => id > 0)
                    .Distinct()
                    .Select(id => id.ToString())
                    .ToArray();

                if (targets.Length == 0)
                {
                    return;
                }

                await hub.Clients.Users(targets)
                    .SendAsync("workspaceChanged", new { type = "notification" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Notifications] push failed: {ex.Message}");
            }
        }
    }
}
