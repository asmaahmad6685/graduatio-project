using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
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

namespace PhysioGuide.Assessment.Tests;

/// <summary>
/// Runs the real controllers and JWT middleware against a disposable, isolated
/// SQLite database. No configured development database is opened or migrated.
/// </summary>
public sealed class AssessmentApiFactory : WebApplicationFactory<graduatio_project.Program>
{
    private const string JwtKey = "AssessmentTests_Only_Key_With_At_Least_32_Bytes_2026";
    private const string JwtIssuer = "PhysioGuide.Assessment.Tests";
    private const string JwtAudience = "PhysioGuide.Assessment.Tests.Client";
    private readonly string _connectionString =
        $"Data Source=assessment-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=True";
    private SqliteConnection? _keeperConnection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // Do not depend on locating the application via a solution file.
        builder.UseContentRoot(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../../graduatio project")));
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = JwtKey,
                ["Jwt:Issuer"] = JwtIssuer,
                ["Jwt:Audience"] = JwtAudience
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(_connectionString));
            // Keep the application's real Bearer scheme and middleware, while
            // guaranteeing test tokens use only this test-specific signing key.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = JwtIssuer,
                    ValidAudience = JwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey))
                };
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
            new User { UserId = 101, Email = "patient-one@example.test", Role = "Patient" },
            new User { UserId = 102, Email = "patient-two@example.test", Role = "Patient" },
            new User { UserId = 103, Email = "no-profile@example.test", Role = "Patient" });
        // Deliberately different IDs catch treating JWT UserId as PatientId.
        db.Patients.AddRange(
            new Patient { PatientId = 201, UserId = 101, FullName = "Test Patient One" },
            new Patient { PatientId = 202, UserId = 102, FullName = "Test Patient Two" });
        await db.SaveChangesAsync();
    }

    public HttpClient CreateAuthenticatedClient(string userId = "101", string role = "Patient")
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Role, role)
            },
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _keeperConnection?.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_keeperConnection is not null) await _keeperConnection.DisposeAsync();
    }
}
