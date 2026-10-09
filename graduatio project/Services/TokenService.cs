using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace graduatio_project.Services
{
    public class TokenService
    {
        //IConfiguration يسمح لنا بقراءة إعدادات JWT
        // الموجودة داخل appsettings.json
        private readonly IConfiguration _configuration;

        //  نستقبل IConfiguration تلقائيًا عن طريق Dependency Injection
        public TokenService(IConfiguration configuration)
        {
            _configuration = configuration;

        }
        // هذه الدالة تنشئ Access Token للمستخدم بعد نجاح تسجيل الدخول
        // وتضع داخلها UserId و Role حتى نعرف هوية المستخدم وصلاحياته
        public string CreateToken(int userId, string role)
        {
            //  نقرأ إعدادات JWT من appsettings.json
            var jwtKey = _configuration["Jwt:Key"];
            var jwtIssuer = _configuration["Jwt:Issuer"];
            var jwtAudience = _configuration["Jwt:Audience"];
            var expireMinutes = int.Parse(
                _configuration["Jwt:ExpireMinutes"] ?? "60"
            );

            //  هذه المعلومات سيتم تخزينها داخل الـ Token
            // UserId يحدد المستخدم و Role يحدد نوع حسابه وصلاحياته
            var claims = new[]
            {
        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
        new Claim(ClaimTypes.Role, role)
    };

            //  نحول الـ Secret Key إلى مفتاح يستخدم لتوقيع الـ Token
            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey!)
            );

            //  نحدد طريقة توقيع الـ Token للتأكد أنها لم يتم التلاعب بها
            var credentials = new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256
            );

            //  ننشئ الـ JWT ونحدد محتوياتها ومدة صلاحيتها
            var token = new JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(expireMinutes),
                signingCredentials: credentials
            );

            //  نحول الـ Token إلى نص يمكن إرساله إلى الـ Frontend
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
