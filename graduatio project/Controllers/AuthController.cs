//  نستخدم DbContext للوصول إلى قاعدة البيانات
using graduatio_project.Data;

// نستخدم DTOs لاستقبال بيانات التسجيل وتسجيل الدخول
using graduatio_project.DTOs.Auth;

//  نستخدم Models لإنشاء User و Patient و Center
using graduatio_project.Models;

//  نستخدم PasswordService لتشفير كلمة المرور والتحقق منها
using graduatio_project.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
//  نستخدم Authorize لحماية الـ APIs ومنع الوصول إليها بدون تسجيل دخول
using Microsoft.AspNetCore.Authorization;

//  نستخدمها لإنشاء Reset Token عشوائية وآمنة
// وعمل Hash للـ Token قبل تخزينها في قاعدة البيانات
using System.Security.Cryptography;

//  نستخدم Encoding لتحويل النص إلى Bytes عند عمل Hash
using System.Text;


namespace graduatio_project.Controllers
{
    // هذا الـ Controller مسؤول عن عمليات Authentication
    // مثل Register و Login و Forgot Password
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        //  نستخدم DbContext للتعامل مع قاعدة البيانات
        private readonly ApplicationDbContext _context;

        //  نستخدم PasswordService لتشفير كلمات المرور والتحقق منها
        private readonly PasswordService _passwordService;

        //  نستخدم TokenService لإنشاء Access Token بعد نجاح تسجيل الدخول
        private readonly TokenService _tokenService;

        //  ASP.NET Core يرسل لنا هذه الخدمات تلقائيًا
        // عن طريق Dependency Injection
        public AuthController(
            ApplicationDbContext context,
            PasswordService passwordService,
            TokenService tokenService)
        {
            _context = context;
            _passwordService = passwordService;
            _tokenService = tokenService;
        }

        [HttpPost("register/patient")]
        public async Task<IActionResult> RegisterPatient(PatientRegisterDto dto)
        {
            //  نتحقق أولًا إذا كان البريد الإلكتروني مستخدمًا من قبل
            bool emailExists = await _context.Users
                .AnyAsync(u => u.Email == dto.Email);

            //  إذا كان البريد موجودًا لا نسمح بإنشاء حساب آخر بنفس البريد
            if (emailExists)
            {
                return BadRequest(new
                {
                    message = "Email is already registered."
                });
            }

            // ملاحظة: ننشئ User جديد يحتوي على بيانات الحساب الأساسية
            var user = new User
            {
                Email = dto.Email,
                PhoneNumber = dto.PhoneNumber,

                //  لا نخزن كلمة المرور الأصلية
                // نخزن فقط الـ Hash الناتج من PasswordService
                PasswordHash = _passwordService.HashPassword(dto.Password),

                //  نحدد نوع الحساب كمريض من داخل الـ Backend
                // ولا نسمح للـ Frontend بتحديد الـ Role
                Role = "Patient",

                //  حساب المريض يكون فعالًا مباشرة بعد التسجيل
                IsActive = true
            };

            //  نضيف الـ User إلى EF Core ليتم حفظه في جدول Users
            _context.Users.Add(user);

            //  نحفظ الـ User الآن حتى تقوم قاعدة البيانات بإنشاء UserId له
            await _context.SaveChangesAsync();

            //  ننشئ سجل Patient يحتوي على بيانات المريض الشخصية
            var patient = new Patient
            {
                //  نربط المريض بحساب الـ User الذي أنشأناه قبل قليل
                UserId = user.UserId,

                //  نخزن الاسم الكامل والجنس القادمين من بيانات التسجيل
                FullName = dto.FullName,
                Gender = dto.Gender
            };

            //  نضيف المريض إلى جدول Patients
            _context.Patients.Add(patient);

            //  نحفظ بيانات المريض في قاعدة البيانات
            await _context.SaveChangesAsync();

            //  نرجع Response للـ Frontend يخبره أن التسجيل نجح
            // مع بعض بيانات الحساب التي تم إنشاؤها
            return Ok(new
            {
                message = "Patient registered successfully.",
                userId = user.UserId,
                patientId = patient.PatientId,
                email = user.Email,
                role = user.Role
            });
        }

        //  هذا الـ API مسؤول عن إنشاء حساب جديد للمركز
        // الرابط سيكون: POST /api/auth/register/center
        [HttpPost("register/center")]
        public async Task<IActionResult> RegisterCenter(CenterRegisterDto dto)
        {
            //  نتحقق أولًا إذا كان البريد الإلكتروني مستخدمًا من قبل
            bool emailExists = await _context.Users
                .AnyAsync(u => u.Email == dto.Email);

            //  إذا كان البريد مستخدمًا لا نسمح بإنشاء حساب آخر بنفس البريد
            if (emailExists)
            {
                return BadRequest(new
                {
                    message = "Email is already registered."
                });
            }

            //  ننشئ User جديد يحتوي على بيانات حساب المركز الأساسية
            var user = new User
            {
                Email = dto.Email,
                PhoneNumber = dto.PhoneNumber,

                //  لا نخزن كلمة المرور الأصلية
                // نخزن فقط الـ Hash الناتج من PasswordService
                PasswordHash = _passwordService.HashPassword(dto.Password),

                //  نحدد نوع الحساب كمركز من داخل الـ Backend
                Role = "Center",

                //  الحساب نفسه يكون فعالًا، لكن المركز يبقى Pending
                // ولا يظهر كمركز معتمد حتى يوافق عليه الـ Admin
                IsActive = true
            };

            //  نضيف حساب المركز إلى جدول Users
            _context.Users.Add(user);

            //  نحفظ الـ User حتى تقوم قاعدة البيانات بإنشاء UserId له
            await _context.SaveChangesAsync();

            //  ننشئ سجل Center ونربطه بحساب الـ User الذي أنشأناه
            var center = new Center
            {
                //  ربط المركز بحساب المستخدم الخاص به
                UserId = user.UserId,

                //  اسم المركز القادم من صفحة التسجيل
                // نخزنه حاليًا في NameArabic لأنه الحقل الأساسي المطلوب في قاعدة البيانات
                NameArabic = dto.CenterName,

                //  نخزن المدينة بشكل منفصل حتى نستخدمها لاحقًا في البحث حسب المدينة
                City = dto.City,

                // Location هو النص الكامل للموقع القادم من واجهة التسجيل
                Address = dto.Location,

                //  الإحداثيات اختيارية وتستخدم للخريطة و Get Directions
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,

                //  أي مركز جديد ينتظر موافقة الـ Admin قبل اعتماده
                Status = "Pending"
            };

            //  نضيف المركز إلى جدول Centers
            _context.Centers.Add(center);

            //  نحفظ بيانات المركز في قاعدة البيانات
            await _context.SaveChangesAsync();

            //  نرجع Response للـ Frontend يؤكد نجاح تسجيل المركز
            // ونوضح أن حالة المركز Pending لأنه ينتظر موافقة الـ Admin
            return Ok(new
            {
                message = "Center registered successfully and is pending admin approval.",
                userId = user.UserId,
                centerId = center.CenterId,
                email = user.Email,
                role = user.Role,
                status = center.Status
            });

        }
        //  هذا الـ API مسؤول عن تسجيل دخول المستخدم
        // الرابط سيكون: POST /api/auth/login
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto dto)
        {
            //  نبحث عن المستخدم في جدول Users باستخدام البريد الإلكتروني
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == dto.Email);

            //  إذا لم نجد حسابًا بهذا البريد نرجع 401 Unauthorized
            if (user == null)
            {
                return Unauthorized(new
                {
                    message = "Invalid email or password."
                });
            }

            //  نتحقق أن كلمة المرور التي أدخلها المستخدم
            // تطابق الـ PasswordHash المخزن في قاعدة البيانات
            bool passwordIsValid = _passwordService.VerifyPassword(
                dto.Password,
                user.PasswordHash
            );

            //  إذا كانت كلمة المرور غير صحيحة نرجع 401 Unauthorized
            if (!passwordIsValid)
            {
                return Unauthorized(new
                {
                    message = "Invalid email or password."
                });
            }
            //  نتحقق أن الحساب فعال ومسموح له بتسجيل الدخول
            if (!user.IsActive)
            {
                //  إذا كان الحساب موقوفًا نمنع تسجيل الدخول
                return Unauthorized(new
                {
                    message = "This account is inactive."
                });
            }

            // ملاحظة: إذا كان المستخدم Center، نتحقق من حالة المركز قبل السماح له بتسجيل الدخول
            if (user.Role == "Center")
            {
                // ملاحظة: نجيب بيانات المركز المرتبطة بحساب المستخدم
                var center = await _context.Centers
                    .FirstOrDefaultAsync(c => c.UserId == user.UserId);

                //  إذا لم نجد بيانات المركز نوقف تسجيل الدخول
                if (center == null)
                {
                    return StatusCode(403, new
                    {
                        message = "Center profile not found."
                    });
                }

                //  المركز الذي لم تتم الموافقة عليه بعد لا يستطيع تسجيل الدخول
                if (center.Status == "Pending")
                {
                    return StatusCode(403, new
                    {
                        message = "Your center account is pending admin approval."
                    });
                }

                //  إذا تم رفض المركز نمنع تسجيل الدخول
                if (center.Status == "Rejected")
                {
                    return StatusCode(403, new
                    {
                        message = "Your center account has been rejected.",
                        rejectionReason = center.RejectionReason
                    });
                }

                //  إذا كان المركز موقوفاً نمنع تسجيل الدخول
                if (center.Status == "Suspended")
                {
                    return StatusCode(403, new
                    {
                        message = "Your center account is suspended."
                    });
                }
            }
            //  بعد نجاح جميع فحوصات تسجيل الدخول
            // ننشئ Access Token تحتوي على UserId و Role
            var token = _tokenService.CreateToken(
                user.UserId,
                user.Role
            );

            //  نرجع للـ Frontend نتيجة تسجيل الدخول
            // مع الـ Access Token وبيانات المستخدم الأساسية
            return Ok(new
            {
                message = "Login successful.",
                accessToken = token,
                userId = user.UserId,
                email = user.Email,
                role = user.Role
            });
        }


       
        // ملاحظة: هذا الـ API يستقبل إيميل المستخدم عندما يضغط Forgot Password
        // ويتأكد أولاً إذا كان هناك حساب مسجل بهذا الإيميل
        [AllowAnonymous]
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordDto dto)
        {
            //  نبحث عن المستخدم في جدول Users باستخدام الإيميل
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == dto.Email);

            //  إذا لم نجد المستخدم، نرجع نفس الرسالة العامة
            // حتى لا نكشف إذا كان الإيميل مسجلاً في النظام أم لا
            if (user == null)
            {
                return Ok(new
                {
                    message = "If the email exists, a password reset link will be sent."
                });
            }
            //  ننشئ Reset Token عشوائية وآمنة
            var resetToken = Convert.ToHexString(
                RandomNumberGenerator.GetBytes(32)
            );

            //  نعمل Hash للـ Reset Token قبل تخزينها في قاعدة البيانات
            var tokenHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(resetToken))
            );
            // ونربطه بالمستخدم الذي طلب إعادة تعيين كلمة المرور
            var passwordResetToken = new PasswordResetToken
            {
                UserId = user.UserId,
                TokenHash = tokenHash,

                // ملاحظة: صلاحية الـ Reset Token هي 30 دقيقة فقط
                ExpiresAt = DateTime.UtcNow.AddMinutes(30)
            };

            // ملاحظة: نضيف السجل إلى قاعدة البيانات ثم نحفظ التغييرات
            _context.PasswordResetTokens.Add(passwordResetToken);
            await _context.SaveChangesAsync();

            // ملاحظة: أثناء التطوير فقط نرجع الـ Reset Token في الـ Response
            // لاحقاً في النظام الحقيقي سيتم إرسالها للمستخدم عبر البريد الإلكتروني
            return Ok(new
            {
                message = "Password reset token created successfully.",
                resetToken = resetToken
            });
        }

            // ملاحظة: هذا الـ API يستخدم الـ Reset Token لتعيين كلمة مرور جديدة
            [AllowAnonymous]
            [HttpPost("reset-password")]
            public async Task<IActionResult> ResetPassword(ResetPasswordDto dto)
            {
                // ملاحظة: نعمل Hash للـ Token التي أرسلها المستخدم
                // حتى نقارنها مع الـ TokenHash المخزنة في قاعدة البيانات
                var tokenHash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(dto.Token))
                );

                // ملاحظة: نبحث عن Reset Token مطابقة في قاعدة البيانات
                var passwordResetToken = await _context.PasswordResetTokens
                    .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

                // ملاحظة: إذا لم نجد الـ Token فهذا يعني أنها غير صحيحة
                if (passwordResetToken == null)
                {
                    return BadRequest(new
                    {
                        message = "Invalid reset token."
                    });
                }

                // ملاحظة: إذا تم استخدام الـ Token سابقاً فلا نسمح باستخدامها مرة ثانية
                if (passwordResetToken.UsedAt != null)
                {
                    return BadRequest(new
                    {
                        message = "This reset token has already been used."
                    });
                }

                // ملاحظة: نتأكد أن مدة صلاحية الـ Token لم تنتهِ
                if (passwordResetToken.ExpiresAt < DateTime.UtcNow)
                {
                    return BadRequest(new
                    {
                        message = "This reset token has expired."
                    });
                }

                // ملاحظة: نبحث عن المستخدم المرتبط بهذه الـ Reset Token
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == passwordResetToken.UserId);

                // ملاحظة: إذا لم نجد المستخدم نوقف العملية
                if (user == null)
                {
                    return BadRequest(new
                    {
                        message = "User not found."
                    });
                }

                // ملاحظة: نعمل Hash لكلمة المرور الجديدة قبل تخزينها
                user.PasswordHash = _passwordService.HashPassword(dto.NewPassword);

                // ملاحظة: نسجل وقت استخدام الـ Reset Token
                // حتى لا يستطيع المستخدم استخدامها مرة ثانية
                passwordResetToken.UsedAt = DateTime.UtcNow;

                // ملاحظة: نحفظ كلمة المرور الجديدة وتحديث الـ Reset Token في قاعدة البيانات
                await _context.SaveChangesAsync();

                // ملاحظة: إذا وصلنا إلى هنا فقد تم تغيير كلمة المرور بنجاح
                return Ok(new
                {
                    message = "Password reset successfully."
                });
            }

        }
    }


