using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace graduatio_project.Hubs
{
    //  قناة الدفع الفورية للمحادثات: كل جلسة متصلة تنضم تلقائيًا إلى
    //  المستخدم المعرَّف في الـ JWT (ادعاء NameIdentifier)، والخادم يبث إليها
    //  عبر Clients.Users(...) أحداث: رسالة جديدة / قراءة / محادثة جديدة
    //  لا توجد طرق يستدعيها العميل: الاتصال فقط للاستقبال، والإرسال يبقى REST
    [Authorize]
    public class MessagingHub : Hub
    {
    }
}
