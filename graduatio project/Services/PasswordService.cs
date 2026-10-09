using Microsoft.AspNetCore.Identity;
namespace graduatio_project.Services
{
    //  هذه الخدمة مسؤولة عن تشفير كلمة المرور والتحقق منها
    public class PasswordService
    {
       
       
            //  PasswordHasher يقوم بإنشاء Hash آمن لكلمة المرور
            private readonly PasswordHasher<object> _passwordHasher = new();

            //  هذه الدالة تستقبل كلمة المرور العادية
            // وترجع Password Hash ليتم تخزينه في قاعدة البيانات
            public string HashPassword(string password)
            {
                return _passwordHasher.HashPassword(null!, password);
            }

            //  هذه الدالة تستخدم عند تسجيل الدخول
            // للتأكد أن كلمة المرور المدخلة تطابق الـ Hash المخزن في قاعدة البيانات
            public bool VerifyPassword(string password, string passwordHash)
            {
                var result = _passwordHasher.VerifyHashedPassword(
                    null!,
                    passwordHash,
                    password
                );

                return result != PasswordVerificationResult.Failed;
            }
        }
    }

