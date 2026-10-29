using Microsoft.AspNetCore.Mvc;

namespace graduatio_project.Services
{
    //  مصنع موحد لكل أخطاء الـ API
    //  هدفنا شكل استجابة واحد: كل خطأ غير Validation يحمل code و message
    //  والخطأ المتعلق بحقل واحد يضيف errors حتى يتم ربطه بالحقل في الواجهة
    public static class ApiError
    {
        //  400 تحقق من المدخلات — الشكل نفسه الذي يعيده ApiController تلقائيًا
        //  حتى لا يتعامل الـ Frontend مع شكلين مختلفين لنفس النوع من الأخطاء
        public static ObjectResult Validation(
            IDictionary<string, string[]> errors,
            string? message = null)
        {
            var body = new Dictionary<string, object?>
            {
                ["title"] = "One or more validation errors occurred.",
                ["status"] = 400,
                ["errors"] = errors
            };

            if (message != null)
            {
                body["message"] = message;
            }

            return new ObjectResult(body) { StatusCode = 400 };
        }

        //  400 بسيط بدون errors (مثل توكن غير صالح)
        public static ObjectResult BadRequest(string code, string message, object? errors = null)
            => Result(400, code, message, errors);

        //  401 — لا يحمل errors إطلاقًا حتى تستطيع الواجهة تمييزه
        public static ObjectResult Unauthorized(string code, string message, object? errors = null)
            => Result(401, code, message, errors);

        //  403 — الحساب أو الدور لا يسمح بالإجراء
        public static ObjectResult Forbidden(string code, string message, object? errors = null)
            => Result(403, code, message, errors);

        //  404 — المورد غير موجود أو خارج نطاق هذا المستخدم
        public static ObjectResult NotFound(string code, string message, object? errors = null)
            => Result(404, code, message, errors);

        //  409 — تعارض منطقي: بريد مكرر، موعد محجوز، إلخ
        public static ObjectResult Conflict(string code, string message, object? errors = null)
            => Result(409, code, message, errors);

        private static ObjectResult Result(int status, string code, string message, object? errors)
        {
            object body = errors == null
                ? new { code, message }
                : new { code, message, errors };

            return new ObjectResult(body) { StatusCode = status };
        }
    }
}
