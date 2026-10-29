using Google.Apis.Auth;
using graduatio_project.Options;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;

namespace graduatio_project.Services
{
    public interface IGoogleTokenValidator
    {
        Task<GoogleJsonWebSignature.Payload?> ValidateAsync(string idToken, string challengeId, string flowVerifier, CancellationToken cancellationToken = default);
    }

    public sealed class GoogleTokenValidator : IGoogleTokenValidator
    {
        private readonly GoogleAuthOptions _options;
        private readonly GoogleNonceStore _nonces;

        public GoogleTokenValidator(IOptions<GoogleAuthOptions> options, GoogleNonceStore nonces)
        {
            _options = options.Value;
            _nonces = nonces;
        }

        // challengeId is a lookup key, never an expected nonce supplied by a client.
        // Invalid credentials return null. Configuration/network failures throw;
        // endpoints handle those as service errors, never authenticate.
        public async Task<GoogleJsonWebSignature.Payload?> ValidateAsync(
            string idToken, string challengeId, string flowVerifier, CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled || !_options.HasValidWebClientId())
            {
                throw new InvalidOperationException("Google authentication is disabled or not configured.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(idToken) || idToken.Length > 10000
                || string.IsNullOrWhiteSpace(challengeId) || challengeId.Length != 64
                || string.IsNullOrWhiteSpace(flowVerifier) || flowVerifier.Length != 64
                || !_nonces.IsPending(challengeId, flowVerifier))
            {
                return null;
            }

            GoogleJsonWebSignature.Payload payload;
            try
            {
                // Google's library validates RS256 signatures against Google's keys,
                // the Google issuer, audience, issued-at time, and expiration.
                payload = await GoogleJsonWebSignature.ValidateAsync(idToken,
                    new GoogleJsonWebSignature.ValidationSettings
                    {
                        Audience = new[] { _options.WebClientId },
                        ExpirationTimeClockTolerance = TimeSpan.Zero
                    }).WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            catch (Exception ex) when (ex is InvalidJwtException or FormatException
                or Newtonsoft.Json.JsonException or ArgumentException)
            {
                return null;
            }

            if (!HasRequiredClaims(payload))
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return _nonces.TryConsume(challengeId, payload.Nonce, flowVerifier) ? payload : null;
        }

        // Called only AFTER Google's cryptographic validation succeeds.
        private bool HasRequiredClaims(GoogleJsonWebSignature.Payload payload)
        {
            return (payload.Issuer == "accounts.google.com" || payload.Issuer == "https://accounts.google.com")
                && payload.AudienceAsList.SequenceEqual(new[] { _options.WebClientId })
                && payload.ExpirationTimeSeconds.HasValue
                && payload.ExpirationTimeSeconds.Value > DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                && payload.EmailVerified && !string.IsNullOrWhiteSpace(payload.Email)
                && payload.Email.Length <= 256 && new EmailAddressAttribute().IsValid(payload.Email)
                && !string.IsNullOrWhiteSpace(payload.Subject) && payload.Subject.Length <= 255
                && !string.IsNullOrWhiteSpace(payload.Nonce) && payload.Nonce.Length == 64;
        }
    }
}
