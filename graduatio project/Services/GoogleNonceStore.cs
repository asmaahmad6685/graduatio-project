using System.Security.Cryptography;
using System.Text;

namespace graduatio_project.Services;

// Single API instance only: replicas require a shared store with atomic consume.
public sealed class GoogleNonceStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    // Stores the pending challenges in memory, timed by the injected clock.
    public GoogleNonceStore(TimeProvider clock) => _clock = clock;

    // Creates a challenge id, nonce and expiry bound to one browser tab.
    public (string ChallengeId, string Nonce, DateTimeOffset ExpiresAt) Issue(string flowBinding)
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            foreach (var key in _entries.Where(e => e.Value.ExpiresAt <= now).Select(e => e.Key).ToArray())
                _entries.Remove(key);
            if (_entries.Count >= 1024)
                throw new GoogleFlowCapacityException();
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var expiry = now.AddMinutes(5);
            _entries.Add(id, new Entry(nonce, flowBinding.ToUpperInvariant(), expiry));
            return (id, nonce, expiry);
        }
    }

    // True while the challenge is still unused and unexpired.
    internal bool IsPending(string id, string verifier)
    {
        lock (_gate) return TryGetPending(id, verifier, out _);
    }

    // Consumes the challenge once the nonce and the tab binding match.
    internal bool TryConsume(string id, string nonce, string verifier)
    {
        lock (_gate)
        {
            return TryGetPending(id, verifier, out var entry)
                && FixedEquals(entry.Nonce, nonce) && _entries.Remove(id);
        }
    }

    // Looks up an unexpired entry and drops it when it has expired.
    private bool TryGetPending(string id, string verifier, out Entry entry)
    {
        if (_entries.TryGetValue(id, out var found))
        {
            if (found.ExpiresAt <= _clock.GetUtcNow()) _entries.Remove(id);
            else if (FixedEquals(found.Binding, Bind(verifier))) { entry = found; return true; }
        }
        entry = null!;
        return false;
    }

    // Hash the exact verifier text; the frontend must send the same text at each step.
    internal static string Bind(string verifier) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));
    // Constant-time comparison of two secrets.
    internal static bool FixedEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    // One pending challenge and the tab binding it belongs to.
    private sealed record Entry(string Nonce, string Binding, DateTimeOffset ExpiresAt);
}

public sealed class GoogleFlowCapacityException : Exception;
