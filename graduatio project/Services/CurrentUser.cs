using System.Security.Claims;

namespace graduatio_project.Services
{
    //  قراءة هوية المستخدم الحالي من الـ JWT
    //  كل الـ Controllers تستخدم نفس الطريقة حتى لا يختلف اسم الـ Claim في مكان ما
    public static class CurrentUser
    {
        public static bool TryGetUserId(ClaimsPrincipal user, out int userId)
        {
            userId = 0;
            var claim = user.FindFirst(ClaimTypes.NameIdentifier);
            return claim != null && int.TryParse(claim.Value, out userId);
        }

        //  الدور داخل التوكن مكتوب بالصيغة الأصلية في قاعدة البيانات
        //  مثل Patient و Center و Therapist و Admin
        public static bool IsRole(ClaimsPrincipal user, string role) => user.IsInRole(role);

        //  شكل الدور كما يعيده الـ API: صغير دائمًا
        public static string MapRole(string role) => role switch
        {
            "Patient" => "patient",
            "Center" => "center",
            "Therapist" => "physiotherapist",
            "Admin" => "admin",
            _ => role.ToLowerInvariant()
        };
    }
}