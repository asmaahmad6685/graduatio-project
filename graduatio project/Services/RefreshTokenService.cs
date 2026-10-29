using System.Security.Cryptography;
using graduatio_project.Data;
using graduatio_project.Models;
using Microsoft.EntityFrameworkCore;

namespace graduatio_project.Services
{
    //  مسؤولة عن إصدار Refresh Token وتجديده وإلغاؤه
    //  القاعدة: لا نخزن التوكن الأصلي أبدًا، فقط SHA-256 منه
    //  وكل استخدام يُصدر توكنًا جديدًا ويُلغي القديم (rotation)
    public class RefreshTokenService
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;

        public RefreshTokenService(ApplicationDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        //  مدة صلاحية الـ Refresh Token بالأيام، تُضبط من appsettings
        private int LifetimeDays =>
            int.TryParse(_configuration["Jwt:RefreshDays"], out var days) ? days : 7;

        //  نصدر توكنًا جديدًا للمستخدم ونرجع النص الخام مرة واحدة فقط
        public async Task<string> IssueAsync(int userId, string? deviceInfo = null)
        {
            var raw = GenerateToken();

            _context.RefreshTokens.Add(new RefreshToken
            {
                UserId = userId,
                TokenHash = Hash(raw),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(LifetimeDays),
                DeviceInfo = deviceInfo
            });

            await _context.SaveChangesAsync();
            return raw;
        }

        //  نتيجة التجديد: نحمل بها التوكن الخام الجديد مرة واحدة فقط
        public sealed record RotatedToken(int UserId, string RawToken);

        //  نجرّب تجديد التوكن. ترجع النص الخام الجديد عند النجاح
        //  وnull إذا كان التوكن غير معروف أو منتهي أو مُلغى
        public async Task<RotatedToken?> TryRotateAsync(string rawToken)
        {
            if (string.IsNullOrWhiteSpace(rawToken))
            {
                return null;
            }

            var hash = Hash(rawToken);
            var existing = await _context.RefreshTokens
                .FirstOrDefaultAsync(t => t.TokenHash == hash);

            if (existing == null)
            {
                return null;
            }

            //  إعادة استخدام توكن مُلغى يعني أن التوكن سُرق
            //  عندها نُلقي كل توكنات هذا المستخدم كإجراء احترازي
            if (existing.RevokedAt != null)
            {
                await RevokeAllForUserAsync(existing.UserId);
                return null;
            }

            if (existing.ExpiresAt <= DateTime.UtcNow)
            {
                return null;
            }

            var newRaw = GenerateToken();
            var newHash = Hash(newRaw);

            existing.RevokedAt = DateTime.UtcNow;
            existing.ReplacedByTokenHash = newHash;

            var replacement = new RefreshToken
            {
                UserId = existing.UserId,
                TokenHash = newHash,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(LifetimeDays),
                DeviceInfo = existing.DeviceInfo
            };
            _context.RefreshTokens.Add(replacement);

            await _context.SaveChangesAsync();

            return new RotatedToken(existing.UserId, newRaw);
        }

        //  نلغي توكنًا واحدًا عند تسجيل الخروج
        public async Task RevokeAsync(string rawToken)
        {
            if (string.IsNullOrWhiteSpace(rawToken))
            {
                return;
            }

            var hash = Hash(rawToken);
            var existing = await _context.RefreshTokens
                .FirstOrDefaultAsync(t => t.TokenHash == hash);

            if (existing == null || existing.RevokedAt != null)
            {
                return;
            }

            existing.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        //  نلغي كل توكنات المستخدم (تغيير كلمة المرور، أو اكتشاف إعادة استخدام)
        public async Task RevokeAllForUserAsync(int userId)
        {
            var tokens = await _context.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null)
                .ToListAsync();

            if (tokens.Count == 0)
            {
                return;
            }

            var now = DateTime.UtcNow;
            foreach (var token in tokens)
            {
                token.RevokedAt = now;
            }

            await _context.SaveChangesAsync();
        }

        //  نصدر توكنًا عشوائيًا بطول 64 حرفًا سداسيًا
        private static string GenerateToken()
            => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        private static string Hash(string value)
            => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    }
}
