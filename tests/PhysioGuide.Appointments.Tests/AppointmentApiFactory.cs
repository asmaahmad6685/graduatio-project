using System.IdentityModel.Tokens.Jwt;
using System.Net;
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

namespace PhysioGuide.Appointments.Tests;

public sealed class FixedTimeProvider : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 17, 6, 30, 0, TimeSpan.Zero);
    public Func<DateTimeOffset>? ReadUtcNow { get; set; }
    public override DateTimeOffset GetUtcNow() => ReadUtcNow?.Invoke() ?? UtcNow;
}

/// <summary>Real HTTP/JWT pipeline. Every test owns an isolated SQLite memory database,
/// never the configured SQL Server database. SQL Server application locks need separate integration coverage.</summary>
public sealed class AppointmentApiFactory : WebApplicationFactory<graduatio_project.Program>
{
    private const string Key = "AppointmentsTests_Only_Key_With_At_Least_32_Bytes_2026";
    private const string Issuer = "PhysioGuide.Appointments.Tests";
    private const string Audience = "PhysioGuide.Appointments.Tests.Client";
    private readonly string _connectionString = $"Data Source=appointments-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=True";
    private SqliteConnection? _keeper;
    public FixedTimeProvider Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../graduatio project")));
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = Key, ["Jwt:Issuer"] = Issuer, ["Jwt:Audience"] = Audience
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
                    ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
                    ValidIssuer = Issuer, ValidAudience = Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key))
                });
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        _keeper = new SqliteConnection(_connectionString);
        await _keeper.OpenAsync();
        await WithDbAsync(async db =>
        {
            await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(
                new User { UserId = 101, Email = "center-one@example.test", Role = "Center" },
                new User { UserId = 102, Email = "center-two@example.test", Role = "Center" },
                new User { UserId = 103, Email = "center-without-profile@example.test", Role = "Center" },
                new User { UserId = 111, Email = "therapist-one@example.test", Role = "Therapist" },
                new User { UserId = 112, Email = "therapist-two@example.test", Role = "Therapist" },
                new User { UserId = 113, Email = "therapist-three@example.test", Role = "Therapist" },
                new User { UserId = 121, Email = "patient-one@example.test", Role = "Patient" },
                new User { UserId = 122, Email = "patient-two@example.test", Role = "Patient" },
                new User { UserId = 123, Email = "patient-without-profile@example.test", Role = "Patient" });
            db.Centers.AddRange(
                new Center { CenterId = 201, UserId = 101, NameArabic = "المركز الأول", NameEnglish = "First center", City = "Test", Address = "Test", Status = "Active" },
                new Center { CenterId = 202, UserId = 102, NameArabic = "المركز الثاني", NameEnglish = "Second center", City = "Test", Address = "Test", Status = "Active" });
            db.Specialties.Add(new Specialty { SpecialtyId = 501, NameArabic = "اختصاص موجود", NameEnglish = "Existing specialty" });
            db.Therapists.AddRange(
                new Therapist { TherapistId = 301, UserId = 111, CenterId = 201, SpecialtyId = 501, FullName = "First therapist", IsActive = true, IsAvailable = true },
                new Therapist { TherapistId = 302, UserId = 112, CenterId = 202, SpecialtyId = 501, FullName = "Other center therapist", IsActive = true, IsAvailable = true },
                new Therapist { TherapistId = 303, UserId = 113, CenterId = 201, SpecialtyId = 501, FullName = "Second same-center therapist", IsActive = true, IsAvailable = true });
            db.Patients.AddRange(
                new Patient { PatientId = 401, UserId = 121, FullName = "First patient" },
                new Patient { PatientId = 402, UserId = 122, FullName = "Second patient" });
            db.Services.AddRange(
                new Service { ServiceId = 601, CenterId = 201, SpecialtyId = 501, NameArabic = "خدمة أولى", NameEnglish = "First service", Price = 123.45m, DurationMinutes = 25, IsActive = true },
                new Service { ServiceId = 602, CenterId = 201, SpecialtyId = 501, NameArabic = "خدمة ثانية", Price = 200m, DurationMinutes = 90, IsActive = true },
                new Service { ServiceId = 603, CenterId = 201, SpecialtyId = 501, NameArabic = "خدمة غير مسندة", Price = 99m, DurationMinutes = 60, IsActive = true },
                new Service { ServiceId = 604, CenterId = 202, SpecialtyId = 501, NameArabic = "خدمة مركز آخر", Price = 150m, DurationMinutes = 60, IsActive = true });
            db.TherapistServices.AddRange(
                new TherapistService { TherapistId = 301, ServiceId = 601 },
                new TherapistService { TherapistId = 301, ServiceId = 602 },
                new TherapistService { TherapistId = 303, ServiceId = 601 },
                new TherapistService { TherapistId = 303, ServiceId = 602 },
                new TherapistService { TherapistId = 302, ServiceId = 604 });
            foreach (var day in Enum.GetValues<DayOfWeek>())
            {
                foreach (var center in new[] { 201, 202 })
                    db.CenterWorkingHours.Add(new CenterWorkingHour { CenterId = center, DayOfWeek = day, OpeningTime = new(8, 0), ClosingTime = new(20, 0) });
                foreach (var therapist in new[] { 301, 302, 303 })
                    db.TherapistSchedules.Add(new TherapistSchedule { TherapistId = therapist, DayOfWeek = day, StartTime = new(9, 0), EndTime = new(20, 0), IsAvailable = true });
            }
            await db.SaveChangesAsync();
            return true;
        });
    }

    public HttpClient Client(string? role = "Patient", string? userId = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (role is null) return client;
        userId ??= role == "Center" ? "101" : role == "Therapist" ? "111" : "121";
        var token = new JwtSecurityToken(Issuer, Audience,
            [new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, role)],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
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
        await WithDbAsync(async db => { action(db); await db.SaveChangesAsync(); return true; });
    }

    public async Task<int> AddAppointmentAsync(string status = "Pending", DateTime? start = null, DateTime? end = null,
        int patient = 401, int therapist = 301, int service = 601, int center = 201)
    {
        return await WithDbAsync(async db =>
        {
            var at = start ?? new DateTime(2026, 10, 18, 10, 0, 0);
            var appointment = new Appointment
            {
                PatientId = patient, CenterId = center, TherapistId = therapist, ServiceId = service,
                StartDateTime = at, EndDateTime = end ?? at.AddMinutes(50), Status = status,
                PriceAtBooking = 77m, PatientNotes = "Existing notes",
                CreatedAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc)
            };
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();
            return appointment.AppointmentId;
        });
    }

    public static object Booking(string start = "2026-10-18T10:00:00", int therapist = 301, int service = 601,
        int center = 201, string? notes = null) => new { centerId = center, serviceId = service, therapistId = therapist, startDateTime = start, patientNotes = notes };

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    public static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response) Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected}; got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_keeper is not null) await _keeper.DisposeAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _keeper?.Dispose();
    }
}
