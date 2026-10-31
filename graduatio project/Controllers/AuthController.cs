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

//  نستخدمها لوضع حدود على عدد محاولات تسجيل الدخول والتسجيل
using Microsoft.AspNetCore.RateLimiting;


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

        //  نستخدمه لإنشاء Refresh Token وتجديده وإلغاؤه
        private readonly RefreshTokenService _refreshTokenService;

        //  ASP.NET Core يرسل لنا هذه الخدمات تلقائيًا
        // عن طريق Dependency Injection
        public AuthController(
            ApplicationDbContext context,
            PasswordService passwordService,
            TokenService tokenService,
            RefreshTokenService refreshTokenService)
        {
            _context = context;
            _passwordService = passwordService;
            _tokenService = tokenService;
            _refreshTokenService = refreshTokenService;
        }

        //  شكل الدور في استجابة الـ API: صغير دائمًا وبالاسم الذي يفهمه الواجهة
        //  الدور داخل الـ JWT يبقى كما هو حتى لا تتغير صلاحيات [Authorize]
        private static string MapRole(string role) => role switch
        {
            "Patient" => "patient",
            "Center" => "center",
            "Therapist" => "physiotherapist",
            "Admin" => "admin",
            _ => role.ToLowerInvariant()
        };

        [EnableRateLimiting("auth-register")]
        [HttpPost("register/patient")]
        public async Task<IActionResult> RegisterPatient(PatientRegisterDto dto)
        {
            //  نتحقق أولًا إذا كان البريد الإلكتروني مستخدمًا من قبل
            bool emailExists = await _context.Users
                .AnyAsync(u => u.Email == dto.Email);

            //  البريد المكرر تعارض منطقي وليست أخطاء تحقق من المدخلات
            //  لذلك نرجع 409 مع errors حتى تربط الواجهة الخطأ بحقل Email
            if (emailExists)
            {
                return ApiError.Conflict(
                    "duplicate_email",
                    "Email is already registered.",
                    //  مفتاح القاموس يبقى كما هو بدون تحويل إلى camelCase
                    //  حتى يطابق اسم الخاصية في الـ C# كما طلب فريق الواجهة
                    new Dictionary<string, string[]>
                    {
                        ["Email"] = new[] { "An account with this email already exists." }
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

            //  نبدأ معاملة واحدة: حساب كامل يعني User و Patient معًا،
            //  فلا يجوز أن يُحفظ أحدهما ويفشل الآخر (patientId = null عند الدخول)
            await using var transaction = await _context.Database.BeginTransactionAsync();

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

            //  لم يفشل أي خطوتين، نؤكد المعاملة
            await transaction.CommitAsync();

            //  نرجع Response للـ Frontend يخبره أن التسجيل نجح
            // مع بعض بيانات الحساب التي تم إنشاؤها
            return Ok(new
            {
                message = "Patient registered successfully.",
                userId = user.UserId,
                //  المعرفات نصوصًا والدور صغيرًا كما في عقد الـ API
                patientId = patient.PatientId.ToString(),
                email = user.Email,
                role = MapRole(user.Role)
            });
        }

        //  هذا الـ API مسؤول عن إنشاء حساب جديد للمركز
        // الرابط سيكون: POST /api/auth/register/center
        [EnableRateLimiting("auth-register")]
        [HttpPost("register/center")]
        public async Task<IActionResult> RegisterCenter(CenterRegisterDto dto)
        {
            //  نتحقق أولًا إذا كان البريد الإلكتروني مستخدمًا من قبل
            bool emailExists = await _context.Users
                .AnyAsync(u => u.Email == dto.Email);

            //  البريد المكرر تعارض منطقي وليست أخطاء تحقق من المدخلات
            //  لذلك نرجع 409 مع errors حتى تربط الواجهة الخطأ بحقل Email
            if (emailExists)
            {
                return ApiError.Conflict(
                    "duplicate_email",
                    "Email is already registered.",
                    //  مفتاح القاموس يبقى كما هو بدون تحويل إلى camelCase
                    //  حتى يطابق اسم الخاصية في الـ C# كما طلب فريق الواجهة
                    new Dictionary<string, string[]>
                    {
                        ["Email"] = new[] { "An account with this email already exists." }
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

            //  معاملة واحدة حتى لا يبقى حساب Center بدون صف Centers
            await using var transaction = await _context.Database.BeginTransactionAsync();

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

            //  ننشئ ساعات عمل افتراضية لمركز جديد حتى يستطيع ضبط أيام عمل معالجيه
            // السبت (6) حتى الخميس (4) ساعات العمل نفسها، والجمعة (5) نصف يوم
            for (var day = DayOfWeek.Sunday; day <= DayOfWeek.Saturday; day++)
            {
                _context.CenterWorkingHours.Add(new CenterWorkingHour
                {
                    CenterId = center.CenterId,
                    DayOfWeek = day,
                    OpeningTime = new TimeOnly(8, 0),
                    ClosingTime = day == DayOfWeek.Friday ? new TimeOnly(13, 0) : new TimeOnly(17, 0),
                    IsClosed = false
                });
            }


            //  لم يفشل أي خطوتين، نؤكد المعاملة
            await transaction.CommitAsync();

            //  نرجع Response للـ Frontend يؤكد نجاح تسجيل المركز
            // ونوضح أن حالة المركز Pending لأنه ينتظر موافقة الـ Admin
            return Ok(new
            {
                message = "Center registered successfully and is pending admin approval.",
                userId = user.UserId,
                //  المعرف نصًا والدور صغيرًا كما في عقد الـ API
                centerId = center.CenterId.ToString(),
                email = user.Email,
                role = MapRole(user.Role),
                status = center.Status
            });

        }
        //  هذا الـ API مسؤول عن تسجيل دخول المستخدم
        // الرابط سيكون: POST /api/auth/login
        [EnableRateLimiting("auth-login")]
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto dto)
        {
            //  نبحث عن المستخدم في جدول Users باستخدام البريد الإلكتروني
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == dto.Email);

            //  الإيميل غير موجود أو كلمة المرور خاطئة: نرجع نفس الاستجابة بالضبط
            //  حتى لا يستطيع أحد تخمين الإيميلات المسجلة
            if (user == null)
            {
                return ApiError.Unauthorized("unauthenticated", "Invalid email or password.");
            }

            //  نتحقق أن كلمة المرور التي أدخلها المستخدم
            // تطابق الـ PasswordHash المخزن في قاعدة البيانات
            bool passwordIsValid = _passwordService.VerifyPassword(
                dto.Password,
                user.PasswordHash
            );

            //  إذا كانت كلمة المرور غير صحيحة نرجع 401 Unauthorized
            //  بلا errors حتى تستطيع الواجهة تمييزها من أخطاء التحقق
            if (!passwordIsValid)
            {
                return ApiError.Unauthorized("unauthenticated", "Invalid email or password.");
            }
            //  نتحقق أن الحساب فعال ومسموح له بتسجيل الدخول
            if (!user.IsActive)
            {
                //  إذا كان الحساب موقوفًا نمنع تسجيل الدخول
                return ApiError.Forbidden("account_not_active", "This account is inactive.");
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
                    return ApiError.Forbidden("center_profile_missing", "Center profile not found.");
                }

                //  المركز الذي لم تتم الموافقة عليه بعد لا يستطيع تسجيل الدخول
                if (center.Status == "Pending")
                {
                    return ApiError.Forbidden("account_not_active",
                        "Your center account is pending admin approval.");
                }

                //  إذا تم رفض المركز نمنع تسجيل الدخول
                if (center.Status == "Rejected")
                {
                    return ApiError.Forbidden("account_rejected",
                        "Your center account has been rejected.",
                        new { rejectionReason = center.RejectionReason });
                }

                //  إذا كان المركز موقوفاً نمنع تسجيل الدخول
                if (center.Status == "Suspended")
                {
                    return ApiError.Forbidden("account_suspended",
                        "Your center account is suspended.");
                }
            }
            if (user.Role == "Therapist")
            {
                var therapist = await _context.Therapists
                    .AsNoTracking()
                    .Include(t => t.Center)
                    .ThenInclude(c => c.User)
                    .FirstOrDefaultAsync(t => t.UserId == user.UserId);

                if (therapist == null || !therapist.IsActive)
                {
                    return StatusCode(403, new
                    {
                        message = "This therapist account cannot sign in."
                    });
                }

                if (therapist.Center == null || therapist.Center.User == null
                    || !therapist.Center.User.IsActive || therapist.Center.Status != "Approved")
                {
                    return StatusCode(403, new
                    {
                        message = "Your center must be approved and active before you can sign in."
                    });
                }
            }

            //  بعد نجاح جميع فحوصات تسجيل الدخول
            // ننشئ Access Token تحتوي على UserId و Role
            var token = _tokenService.CreateToken(
                user.UserId,
                user.Role
            );

            //  نصدر Refresh Token ونخزّن في القاعدة فقط الـ Hash منه
            var refreshToken = await _refreshTokenService.IssueAsync(user.UserId);

            //  نجلب الملف المرتبط بالحساب لنعيد الاسم الكامل ومعرف الدور
            int? patientId = null;
            int? centerId = null;
            int? therapistId = null;
            var fullName = user.Email;

            if (user.Role == "Patient")
            {
                var profile = await _context.Patients
                    .FirstOrDefaultAsync(p => p.UserId == user.UserId);
                if (profile != null)
                {
                    patientId = profile.PatientId;
                    fullName = profile.FullName;
                }
            }
            else if (user.Role == "Center")
            {
                var profile = await _context.Centers
                    .FirstOrDefaultAsync(c => c.UserId == user.UserId);
                if (profile != null)
                {
                    centerId = profile.CenterId;
                    fullName = string.IsNullOrWhiteSpace(profile.NameEnglish)
                        ? profile.NameArabic
                        : profile.NameEnglish;
                }
            }
            else if (user.Role == "Therapist")
            {
                var profile = await _context.Therapists
                    .FirstOrDefaultAsync(t => t.UserId == user.UserId);
                if (profile != null)
                {
                    therapistId = profile.TherapistId;
                    //  الواجهة تحتاج مركز المعالج أيضًا لتوجيهه لبوابته
                    centerId = profile.CenterId;
                    fullName = profile.FullName;
                }
            }
            else if (user.Role == "Admin")
            {
                var profile = await _context.Admins
                    .FirstOrDefaultAsync(a => a.UserId == user.UserId);
                if (profile != null)
                {
                    fullName = profile.FullName;
                }
            }

            //  نرجع للـ Frontend نتيجة تسجيل الدخول
            //  ملاحظة: userId يبقى رقمًا كما كان حتى لا ينكسر العقد الحالي
            //  أما معرفات الدور فتعود نصوصًا كما طلب فريق الواجهة
            return Ok(new
            {
                message = "Login successful.",
                accessToken = token,
                refreshToken,
                expiresIn = _tokenService.AccessTokenLifetimeSeconds,
                userId = user.UserId,
                patientId = patientId?.ToString(),
                centerId = centerId?.ToString(),
                therapistId = therapistId?.ToString(),
                fullName,
                email = user.Email,
                role = MapRole(user.Role)
            });
        }


       
        // ملاحظة: هذا الـ API يستقبل إيميل المستخدم عندما يضغط Forgot Password
        // ويتأكد أولاً إذا كان هناك حساب مسجل بهذا الإيميل
        [AllowAnonymous]
        [EnableRateLimiting("auth-forgot")]
        [HttpPost("forgot-password")]
        [NonAction] // Password recovery is disabled; this method is not an HTTP action.
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
            [NonAction] // Password recovery is disabled; this method is not an HTTP action.
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

        //  هذا الـ API يجدد الـ Access Token قبل انتهاء صلاحيته
        //  الرابط: POST /api/Auth/refresh
        [AllowAnonymous]
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh(RefreshTokenDto dto)
        {
            //  نحاول تجديد التوكن؛ إذا فشل يكون التوكن غير صالح أو مُلغى
            //  أو أن أحدهم أعاد استخدامه بعد تجديد، وعندها نُلغي كل توكنات المستخدم
            var rotated = await _refreshTokenService.TryRotateAsync(dto.RefreshToken);

            if (rotated == null)
            {
                return ApiError.Unauthorized(
                    "invalid_refresh_token",
                    "Refresh token is invalid or has expired.");
            }

            //  ننشئ Access Token جديد للمستخدم نفسه
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == rotated.UserId);

            if (user == null || !user.IsActive)
            {
                return ApiError.Unauthorized(
                    "invalid_refresh_token",
                    "Refresh token is invalid or has expired.");
            }

            var accessToken = _tokenService.CreateToken(user.UserId, user.Role);

            return Ok(new
            {
                message = "Token refreshed successfully.",
                accessToken,
                refreshToken = rotated.RawToken,
                expiresIn = _tokenService.AccessTokenLifetimeSeconds
            });
        }

        //  هذا الـ API يُلغي Refresh Token الحالي عند تسجيل الخروج
        //  الرابط: POST /api/Auth/logout
        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout(LogoutDto dto)
        {
            //  إلغاء توكن غير موجود ليس خطأ: الهدف هو أن يصبح الطلب بلا أثر
            //  لذلك نرجع 204 في الحالتين ليبقى الـ API idempotent
            if (!string.IsNullOrWhiteSpace(dto.RefreshToken))
            {
                await _refreshTokenService.RevokeAsync(dto.RefreshToken);
            }

            return NoContent();
        }

        //  هذا الـ API يغيّر كلمة المرور للمستخدم المسجل دخوله
        //  الرابط: POST /api/Auth/change-password
        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (userIdClaim == null)
            {
                return ApiError.Unauthorized("unauthenticated", "Authentication is required.");
            }

            if (!int.TryParse(userIdClaim.Value, out var userId))
            {
                return ApiError.Unauthorized("unauthenticated", "Authentication is required.");
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                return ApiError.NotFound("not_found", "User not found.");
            }

            //  نتحقق من كلمة المرور الحالية قبل أي تغيير
            if (!_passwordService.VerifyPassword(dto.CurrentPassword, user.PasswordHash))
            {
                return ApiError.Validation(
                    new Dictionary<string, string[]>
                    {
                        ["CurrentPassword"] = new[] { "Current password is incorrect." }
                    },
                    "Current password is incorrect.");
            }

            //  نغيّر كلمة المرور ونخزّن الـ Hash فقط
            user.PasswordHash = _passwordService.HashPassword(dto.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;

            //  نُلغي كل التوكنات المجدولة حتى تُجبر العملية تسجيل الخروج
            //  من كل الأجهزة الأخرى
            await _refreshTokenService.RevokeAllForUserAsync(userId);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Password changed successfully."
            });
        }

        }
    }


