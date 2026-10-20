using Microsoft.EntityFrameworkCore;
using graduatio_project.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using graduatio_project.Services;
using graduatio_project.Options;
//  ‰” Œœ„ OpenApi Õ Ï ‰⁄—¯› Swagger ⁄·Ï JWT Authentication
using Microsoft.OpenApi.Models;
namespace graduatio_project
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            const string localFrontendCorsPolicy = "LocalFrontend";

            // Add services to the container.

            builder.Services.AddControllers();
            builder.Services.AddCors(options =>
            {
                options.AddPolicy(localFrontendCorsPolicy, policy =>
                    policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
                        .WithMethods("GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                        .AllowAnyHeader());
            });
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("PhysioGuideConnection")
    )
);
            //   ”ÃÌ· PasswordService Õ Ï ‰” ÿÌ⁄ «” Œœ«„Â« œ«Œ· «·‹ Controllers
            builder.Services.AddScoped<PasswordService>();

            //  ”ÃÌ· TokenService Õ Ï ‰” ÿÌ⁄ ≈‰‘«¡ Access Token œ«Œ· «·‹ Controllers
            builder.Services.AddScoped<TokenService>();

            // Disabled until a real Web Client ID is configured. Enabling with
            // invalid settings fails at startup; existing password auth is untouched.
            builder.Services.AddOptions<GoogleAuthOptions>()
                .Bind(builder.Configuration.GetSection(GoogleAuthOptions.SectionName))
                .Validate(options => !options.Enabled || options.HasValidWebClientId(),
                    "GoogleAuth:WebClientId must be a configured Google Web Client ID when enabled.")
                .ValidateOnStart();
            builder.Services.AddSingleton<GoogleNonceStore>();
            builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
            builder.Services.AddSingleton<IGoogleTokenValidator, GoogleTokenValidator>();
            builder.Services.AddSingleton<GoogleRegistrationTicketStore>();
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = async (context, ct) =>
                {
                    context.HttpContext.Response.Headers.CacheControl = "no-store";
                    context.HttpContext.Response.Headers.RetryAfter = "60";
                    await context.HttpContext.Response.WriteAsJsonAsync(new
                    {
                        message = "Too many Google authentication requests. Try again later.",
                        code = "rate_limit_exceeded"
                    }, cancellationToken: ct);
                };
                options.AddPolicy("GoogleAuth", context =>
                    System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                            AutoReplenishment = true
                        }));
            });


            var jwtKey = builder.Configuration["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
            {
                throw new InvalidOperationException(
                    "Jwt:Key must be configured with at least 32 UTF-8 bytes. Use User Secrets in Development or the Jwt__Key environment variable.");
            }
            var jwtIssuer = builder.Configuration["Jwt:Issuer"];
            var jwtAudience = builder.Configuration["Jwt:Audience"];

            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,

        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtKey!)
        )
    };
});
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            //  ‰ÃÂ“ Swagger Õ Ï Ì”„Õ ·‰« »≈—”«· JWT Access Token
            // Ê Ã—»… «·‹ APIs «·„Õ„Ì… √À‰«¡  ÿÊÌ— «·‹ Backend
            builder.Services.AddSwaggerGen(options =>
            {
                //  ‰⁄—¯› Swagger √‰ ‰Ÿ«„  ”ÃÌ· «·œŒÊ· Ì” Œœ„ Bearer Token
                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Enter your JWT Access Token"
                });

                //  ‰ÿ·» „‰ Swagger ≈—”«· «·‹ Token „⁄ «·‹ APIs «· Ì  Õ «Ã Authorization
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

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();
            app.UseRouting();
            if (app.Environment.IsDevelopment())
            {
                app.UseCors(localFrontendCorsPolicy);
            }
            app.UseRateLimiter();

            app.UseAuthentication();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
