using Microsoft.EntityFrameworkCore;
using graduatio_project.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using graduatio_project.Services;
//  ������ OpenApi ��� ����� Swagger ��� JWT Authentication
using Microsoft.OpenApi.Models;

//  نستخدمها لضبط حدود الطلبات حتى لا يستطيع أحد إغراق الـ API
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

//  نستخدمها لالتقاط أي خطأ غير متوقع وتحويله إلى شكل الاستجابة الموحد
using Microsoft.AspNetCore.Diagnostics;
namespace graduatio_project
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            //  نقبل المعرف في الطلبات كرقم أو كنص لأن الواجهة ترسله نصوصًا
            //  والاستجابة تبقى رقمًا دائمًا حتى لا يتغير العقد
            builder.Services.AddControllers().AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new FlexibleIntConverter());
                options.JsonSerializerOptions.Converters.Add(new FlexibleNullableIntConverter());
            });
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(
                    builder.Configuration.GetConnectionString("PhysioGuideConnection")
                )
            );
            //  ����� PasswordService ��� ������ ��������� ���� ��� Controllers 
            builder.Services.AddScoped<PasswordService>();

            // ����� TokenService ��� ������ ����� Access Token ���� ��� Controllers
            builder.Services.AddScoped<TokenService>();
            builder.Services.AddScoped<AssessmentService>();
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<SchedulingClock>();
            builder.Services.AddScoped<ScheduleService>();
            builder.Services.AddScoped<AvailabilityService>();
            builder.Services.AddScoped<SchedulingWriteCoordinator>();
            builder.Services.AddScoped<AppointmentService>();


            //  نحن لن نسجّل خروجًا من الخادم بحذف الجلسة لأن الـ JWT stateless
            //  لكننا نلغي الـ Refresh Token المقابل عند logout
            builder.Services.AddScoped<RefreshTokenService>();

            var jwtKey = builder.Configuration["Jwt:Key"];
            var jwtIssuer = builder.Configuration["Jwt:Issuer"];
            var jwtAudience = builder.Configuration["Jwt:Audience"];

            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;

            }).AddJwtBearer(options =>{

                 options.TokenValidationParameters = new TokenValidationParameters
                 {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,

                        ValidIssuer = jwtIssuer,
                        ValidAudience = jwtAudience,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey!))
                 };

                 //  نتحكم بنفسنا في شكل 401 و 403 حتى يحمل كل منها code ثابتًا
                 //  والواجهة تستطيع من خلاله التمييز بين "التوكن منتهي" و "غير مسجل دخول"
                 options.Events = new JwtBearerEvents
                 {
                     OnAuthenticationFailed = context =>
                     {
                         if (context.Exception is SecurityTokenExpiredException)
                         {
                             context.HttpContext.Items["TokenExpired"] = true;
                         }
                         return Task.CompletedTask;
                     },

                     OnChallenge = async context =>
                     {
                         //  نمنع السلوك الافتراضي ونكتب استجابتنا بدلاً منه
                         context.HandleResponse();
                         context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                         context.Response.ContentType = "application/json";

                         var body = context.HttpContext.Items.ContainsKey("TokenExpired")
                             ? "{\"code\":\"token_expired\",\"message\":\"Access token has expired.\"}"
                             : "{\"code\":\"unauthenticated\",\"message\":\"Authentication is required.\"}";

                         await context.Response.WriteAsync(body);
                     },

                     OnForbidden = async context =>
                     {
                         context.Response.StatusCode = StatusCodes.Status403Forbidden;
                         context.Response.ContentType = "application/json";
                         await context.Response.WriteAsync(
                             "{\"code\":\"forbidden\",\"message\":\"You do not have permission to perform this action.\"}");
                     }
                 };
            });
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            //  ���� Swagger ��� ���� ��� ������ JWT Access Token
            // ������ ��� APIs ������� ����� ����� ��� Backend
            builder.Services.AddSwaggerGen(options =>
            {
                //  ����� Swagger �� ���� ����� ������ ������ Bearer Token
                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Enter your JWT Access Token"
                });

                //  ���� �� Swagger ����� ��� Token �� ��� APIs ���� ����� Authorization
                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        Array.Empty<string>()
                    }
                });
            });

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                {
                    policy
                        .AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowAnyHeader();
                });
            });

            //  حدود المعدل: نضعها على نقاط المصادقة فقط
            //  لأنها الأكثر عرضة للمحاولة المتكررة لخانة الرمز السري
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                options.AddFixedWindowLimiter("auth-login", opt =>
                {
                    opt.PermitLimit = 10;
                    opt.Window = TimeSpan.FromMinutes(1);
                    opt.QueueLimit = 0;
                });

                options.AddFixedWindowLimiter("auth-register", opt =>
                {
                    opt.PermitLimit = 5;
                    opt.Window = TimeSpan.FromMinutes(1);
                    opt.QueueLimit = 0;
                });

                options.AddFixedWindowLimiter("auth-forgot", opt =>
                {
                    opt.PermitLimit = 3;
                    opt.Window = TimeSpan.FromMinutes(1);
                    opt.QueueLimit = 0;
                });

                //  نعيد شكل الاستجابة الموحد مع ترويسة Retry-After بالثواني
                options.OnRejected = async (context, cancellationToken) =>
                {
                    var retryAfter = TimeSpan.FromSeconds(60);
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var after))
                    {
                        retryAfter = after;
                    }

                    var response = context.HttpContext.Response;
                    response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                    response.ContentType = "application/json";
                    await response.WriteAsync(
                        "{\"code\":\"rate_limited\",\"message\":\"Too many requests. Try again later.\"}",
                        cancellationToken);
                };
            });

            var app = builder.Build();

            //  نلتقط أي استثناء غير متوقع ونرجع 500 بالشكل الموحد
            //  والتفاصيل الكاملة تذهب إلى الـ Logs فقط ولا تظهر للعميل
            app.UseExceptionHandler(handler => handler.Run(async context =>
            {
                var feature = context.Features.Get<IExceptionHandlerFeature>();
                var logger = context.RequestServices
                    .GetRequiredService<ILogger<Program>>();

                if (feature?.Error != null)
                {
                    logger.LogError(feature.Error, "Unhandled exception on {Path}",
                        context.Request.Path);
                }

                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    "{\"code\":\"internal_error\",\"message\":\"An unexpected error occurred.\"}");
            }));

            app.UseCors("AllowAll");
            app.UseRateLimiter();

            //  ننشر الـ Swagger في كل البيئات حتى يستطيع فريق الواجهة فتحه من الإنتاج
            app.UseSwagger();
            app.UseSwaggerUI();

            //  لملفات الصور المرفوعة (uploads)
            app.UseStaticFiles();

            app.UseHttpsRedirection();
            app.UseAuthentication();

            app.UseAuthorization();


            app.MapControllers();

            app.MapGet("/", () => "Physio Guide API is running!");

            //  ملء القاعدة ببيانات تجريبية عند أول تشغيل فقط
            //  إذا فشل الاتصال نطبع الخطأ ولا نمنع تشغيل الـ API
            using (var scope = app.Services.CreateScope())
            {
                try
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();
                    DbSeeder.SeedAsync(db, passwordService).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Seed] Skipped: {ex.Message}");
                }
            }

            app.Run();
        }
    }
}
