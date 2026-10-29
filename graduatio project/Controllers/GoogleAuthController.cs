using graduatio_project.Data;
using graduatio_project.DTOs.Auth;
using graduatio_project.Models;
using graduatio_project.Options;
using graduatio_project.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Data.Common;

namespace graduatio_project.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/Auth/google")]
[EnableRateLimiting("GoogleAuth")]
[RequestSizeLimit(32768)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GoogleAuthController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IGoogleTokenValidator _validator;
    private readonly GoogleNonceStore _nonces;
    private readonly GoogleRegistrationTicketStore _tickets;
    private readonly TokenService _tokens;
    private readonly GoogleAuthOptions _options;
    private readonly ILogger<GoogleAuthController> _logger;

    public GoogleAuthController(ApplicationDbContext db, IGoogleTokenValidator validator,
        GoogleNonceStore nonces, GoogleRegistrationTicketStore tickets, TokenService tokens,
        IOptions<GoogleAuthOptions> options, ILogger<GoogleAuthController> logger)
    {
        _db = db; _validator = validator; _nonces = nonces; _tickets = tickets;
        _tokens = tokens; _options = options.Value; _logger = logger;
    }

    [HttpPost("challenge")]
    public Task<IActionResult> Challenge(GoogleChallengeDto dto) => ExecuteAsync(() =>
    {
        var challenge = _nonces.Issue(dto.FlowBinding);
        return Task.FromResult<IActionResult>(Ok(new
        {
            challengeId = challenge.ChallengeId, nonce = challenge.Nonce, expiresAt = challenge.ExpiresAt
        }));
    });

    [HttpPost("sign-in")]
    public Task<IActionResult> SignIn(GoogleSignInDto dto, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var identity = await _validator.ValidateAsync(dto.IdToken, dto.ChallengeId, dto.FlowVerifier, ct);
        if (identity is null) return Failure(401, "invalid_google_credentials", "Google sign-in could not be verified. Start again.");

        var login = await _db.ExternalLogins.AsNoTracking().Include(e => e.User)
            .SingleOrDefaultAsync(e => e.Provider == "Google" && e.ProviderSubject == identity.Subject, ct);
        if (login is not null)
        {
            var user = login.User;
            if (!user.IsActive) return Failure(403, "account_unavailable", "This account cannot sign in.");
            if (user.Role == "Center")
            {
                if (!await _db.Centers.AnyAsync(c => c.UserId == user.UserId && c.Status == "Approved", ct))
                    return Failure(403, "center_not_approved", "The center must be approved before signing in.");
            }
            else if (user.Role != "Patient" || !await _db.Patients.AnyAsync(p => p.UserId == user.UserId, ct))
                return Failure(403, "account_unavailable", "This account cannot sign in.");

            return Ok(new
            {
                message = "Login successful.", accessToken = _tokens.CreateToken(user.UserId, user.Role),
                userId = user.UserId, email = user.Email, role = user.Role
            });
        }

        if (await EmailExistsAsync(identity.Email, ct)) return AccountConflict();
        var ticket = _tickets.Issue(new GoogleRegistrationIdentity(identity.Subject, identity.Email), dto.FlowVerifier);
        return Ok(new
        {
            message = "Complete your Patient or Center profile.", requiresRegistration = true,
            registrationTicket = ticket.Ticket, expiresAt = ticket.ExpiresAt, email = identity.Email
        });
    });

    [HttpPost("register/patient")]
    public Task<IActionResult> RegisterPatient(GooglePatientRegisterDto dto, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var identity = _tickets.TryTake(dto.RegistrationTicket, dto.FlowVerifier);
        if (identity is null) return InvalidTicket();
        if (await IdentityExistsAsync(identity, ct)) return AccountConflict();

        var user = NewUser(identity, dto.PhoneNumber, "Patient");
        var patient = new Patient { User = user, FullName = dto.FullName.Trim(), Gender = dto.Gender?.Trim() };
        _db.Patients.Add(patient);
        _db.ExternalLogins.Add(NewLogin(identity, user));

        // Explicit transaction also rolls back if JWT creation fails after SaveChanges.
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await _db.SaveChangesAsync(ct);
        var token = _tokens.CreateToken(user.UserId, user.Role);
        await transaction.CommitAsync(ct);
        return StatusCode(201, new
        {
            message = "Patient registered successfully.", accessToken = token, userId = user.UserId,
            patientId = patient.PatientId, email = user.Email, role = user.Role
        });
    });

    [HttpPost("register/center")]
    public Task<IActionResult> RegisterCenter(GoogleCenterRegisterDto dto, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var identity = _tickets.TryTake(dto.RegistrationTicket, dto.FlowVerifier);
        if (identity is null) return InvalidTicket();
        if (await IdentityExistsAsync(identity, ct)) return AccountConflict();

        var user = NewUser(identity, dto.PhoneNumber, "Center");
        var center = new Center
        {
            User = user, NameArabic = dto.CenterName.Trim(), City = dto.City.Trim(),
            Address = dto.Location.Trim(), Latitude = dto.Latitude, Longitude = dto.Longitude, Status = "Pending"
        };
        _db.Centers.Add(center);
        _db.ExternalLogins.Add(NewLogin(identity, user));
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return StatusCode(201, new
        {
            message = "Center registered successfully and is pending admin approval.", userId = user.UserId,
            centerId = center.CenterId, email = user.Email, role = user.Role, status = center.Status
        });
    });

    private Task<bool> EmailExistsAsync(string email, CancellationToken ct)
    {
        var normalized = email.ToUpperInvariant();
        return _db.Users.AnyAsync(u => u.Email.ToUpper() == normalized, ct);
    }

    private async Task<bool> IdentityExistsAsync(GoogleRegistrationIdentity identity, CancellationToken ct) =>
        await _db.ExternalLogins.AnyAsync(e => e.Provider == "Google" && e.ProviderSubject == identity.Subject, ct)
        || await EmailExistsAsync(identity.Email, ct);

    private static User NewUser(GoogleRegistrationIdentity identity, string phone, string role) => new()
    {
        Email = identity.Email, PhoneNumber = phone.Trim(), PasswordHash = null, Role = role, IsActive = true
    };
    private static ExternalLogin NewLogin(GoogleRegistrationIdentity identity, User user) => new()
    {
        User = user, Provider = "Google", ProviderSubject = identity.Subject
    };

    private ObjectResult InvalidTicket() => Failure(401, "invalid_registration_ticket", "Registration ticket is invalid, expired, or used. Start Google sign-in again.");
    private ObjectResult AccountConflict() => Failure(409, "account_conflict", "An account already exists. Sign in using its existing method; accounts are not automatically linked.");
    private ObjectResult Failure(int status, string code, string message) => StatusCode(status, new { message, code });

    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
    {
        if (!_options.Enabled || !_options.HasValidWebClientId())
            return Failure(503, "google_auth_unavailable", "Google authentication is unavailable.");
        try { return await action(); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Unique email/provider constraints are authoritative across distinct tickets and requests.
            return AccountConflict();
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or GoogleFlowCapacityException
            or DbException or DbUpdateException)
        {
            // Never include tokens, email addresses, database details, or exception text in responses/logs.
            _logger.LogWarning("Google authentication dependency failed ({FailureType}).", ex.GetType().Name);
            return Failure(503, "google_auth_unavailable", "Google authentication is temporarily unavailable. Start again.");
        }
    }
}
