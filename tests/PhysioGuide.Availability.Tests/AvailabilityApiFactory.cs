using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using graduatio_project.Data;
using graduatio_project.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace PhysioGuide.Availability.Tests;

public sealed class FixedTimeProvider : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 17, 6, 30, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => UtcNow;
}

/// <summary>
/// Real HTTP/controller/JWT pipeline with an isolated SQLite in-memory database.
/// Database initialization here can never connect to the configured development DB.
/// </summary>
public sealed class AvailabilityApiFactory : WebApplicationFactory<graduatio_project.Program>
{
    private const string JwtKey = "AvailabilityTests_Only_Key_With_At_Least_32_Bytes_2026";
    private const string JwtIssuer = "PhysioGuide.Availability.Tests";
    private const string JwtAudience = "PhysioGuide.Availability.Tests.Client";
    private readonly string _connectionString = $"Data Source=availability-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=True";
    private SqliteConnection? _keeperConnection;
    public FixedTimeProvider Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../graduatio project")));
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = JwtKey,
            ["Jwt:Issuer"] = JwtIssuer,
            ["Jwt:Audience"] = JwtAudience
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(_connectionString));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = JwtIssuer,
                    ValidAudience = JwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey))
                });
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        _keeperConnection = new SqliteConnection(_connectionString);
        await _keeperConnection.OpenAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(
            new User { UserId = 101, Email = "center-one@example.test", Role = "Center" },
            new User { UserId = 102, Email = "center-two@example.test", Role = "Center" },
            new User { UserId = 103, Email = "center-no-profile@example.test", Role = "Center" },
            new User { UserId = 111, Email = "therapist-one@example.test", Role = "Therapist" },
            new User { UserId = 112, Email = "therapist-two@example.test", Role = "Therapist" },
            new User { UserId = 121, Email = "patient@example.test", Role = "Patient" });
        db.Centers.AddRange(
            new Center { CenterId = 201, UserId = 101, NameArabic = "المركز الأول", City = "Test", Address = "Test", Status = "Active" },
            new Center { CenterId = 202, UserId = 102, NameArabic = "المركز الثاني", City = "Test", Address = "Test", Status = "Active" });
        db.Specialties.Add(new Specialty { SpecialtyId = 501, NameArabic = "اختصاص موجود", NameEnglish = "Existing Specialty" });
        db.Therapists.AddRange(
            new Therapist { TherapistId = 301, UserId = 111, CenterId = 201, SpecialtyId = 501, FullName = "First therapist", IsActive = true, IsAvailable = true },
            new Therapist { TherapistId = 302, UserId = 112, CenterId = 202, SpecialtyId = 501, FullName = "Second therapist", IsActive = true, IsAvailable = true });
        db.Patients.Add(new Patient { PatientId = 401, UserId = 121, FullName = "Patient" });
        db.Services.AddRange(
            new Service { ServiceId = 601, CenterId = 201, SpecialtyId = 501, NameArabic = "خدمة أولى", DurationMinutes = 25, IsActive = true },
            new Service { ServiceId = 602, CenterId = 201, SpecialtyId = 501, NameArabic = "خدمة ثانية", DurationMinutes = 90, IsActive = true },
            new Service { ServiceId = 603, CenterId = 201, SpecialtyId = 501, NameArabic = "خدمة غير مسندة", DurationMinutes = 60, IsActive = true },
            new Service { ServiceId = 604, CenterId = 202, SpecialtyId = 501, NameArabic = "خدمة مركز آخر", DurationMinutes = 60, IsActive = true });
        db.TherapistServices.AddRange(
            new TherapistService { TherapistId = 301, ServiceId = 601 },
            new TherapistService { TherapistId = 301, ServiceId = 602 },
            new TherapistService { TherapistId = 302, ServiceId = 604 });
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            db.CenterWorkingHours.Add(new CenterWorkingHour { CenterId = 201, DayOfWeek = day, OpeningTime = new(8, 0), ClosingTime = new(20, 0) });
            db.CenterWorkingHours.Add(new CenterWorkingHour { CenterId = 202, DayOfWeek = day, OpeningTime = new(8, 0), ClosingTime = new(20, 0) });
        }
        await db.SaveChangesAsync();
    }

    public HttpClient Client(string? role = "Patient", string? userId = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (role is null) return client;
        userId ??= role == "Center" ? "101" : role == "Therapist" ? "111" : "121";
        var token = new JwtSecurityToken(JwtIssuer, JwtAudience,
            [new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, role)],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    public async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    public async Task MutateAsync(Action<ApplicationDbContext> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        action(db);
        await db.SaveChangesAsync();
    }

    public async Task<int> AddWindowAsync(int day = 0, string start = "09:00", string end = "12:00", bool available = true, int therapistId = 301)
    {
        return await WithDbAsync(async db =>
        {
            var window = new TherapistSchedule { TherapistId = therapistId, DayOfWeek = (DayOfWeek)day, StartTime = TimeOnly.Parse(start), EndTime = TimeOnly.Parse(end), IsAvailable = available };
            db.TherapistSchedules.Add(window);
            await db.SaveChangesAsync();
            return window.ScheduleId;
        });
    }

    public async Task<int> AddAppointmentAsync(string status, DateTime start, DateTime? end = null, int serviceId = 601, int therapistId = 301)
    {
        return await WithDbAsync(async db =>
        {
            var appointment = new Appointment
            {
                PatientId = 401, CenterId = therapistId == 301 ? 201 : 202, TherapistId = therapistId,
                ServiceId = serviceId, StartDateTime = start, EndDateTime = end ?? start.AddMinutes(50),
                Status = status, PriceAtBooking = 123.45m, PatientNotes = "Existing booking must remain unchanged",
                CreatedAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc)
            };
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();
            return appointment.AppointmentId;
        });
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    public static object Window(int day = 0, string start = "09:00:00", string end = "12:00:00", bool available = true) =>
        new { dayOfWeek = day, startTime = start, endTime = end, isAvailable = available };

    public static async Task AssertStatusAsync(HttpResponseMessage response, System.Net.HttpStatusCode expected)
    {
        using (response) Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected}; got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_keeperConnection is not null) await _keeperConnection.DisposeAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _keeperConnection?.Dispose();
    }
}
