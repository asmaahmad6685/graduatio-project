using System.ComponentModel.DataAnnotations;

namespace graduatio_project.DTOs.Auth;

public sealed class GoogleChallengeDto
{
    // SHA-256 (hex) of a fresh 32-byte browser-generated flow verifier.
    [Required, RegularExpression("^[A-Fa-f0-9]{64}$")]
    public string FlowBinding { get; set; } = string.Empty;
}

public sealed class GoogleSignInDto
{
    [Required, MaxLength(10000)]
    public string IdToken { get; set; } = string.Empty;

    [Required, RegularExpression("^[A-Fa-f0-9]{64}$")]
    public string ChallengeId { get; set; } = string.Empty;

    [Required, RegularExpression("^[A-Fa-f0-9]{64}$")]
    public string FlowVerifier { get; set; } = string.Empty;
}

public class GoogleRegistrationDto
{
    [Required, RegularExpression("^[A-Fa-f0-9]{64}$")]
    public string RegistrationTicket { get; set; } = string.Empty;

    [Required, RegularExpression("^[A-Fa-f0-9]{64}$")]
    public string FlowVerifier { get; set; } = string.Empty;

    [Required, MaxLength(20), Phone]
    public string PhoneNumber { get; set; } = string.Empty;
}

public sealed class GooglePatientRegisterDto : GoogleRegistrationDto
{
    [Required, MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Gender { get; set; }
}

public sealed class GoogleCenterRegisterDto : GoogleRegistrationDto
{
    [Required, MaxLength(200)]
    public string CenterName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string City { get; set; } = string.Empty;

    [Required, MaxLength(300)]
    public string Location { get; set; } = string.Empty;

    [Range(-90d, 90d)]
    public double? Latitude { get; set; }

    [Range(-180d, 180d)]
    public double? Longitude { get; set; }
}
