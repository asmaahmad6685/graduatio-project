using System.Security.Cryptography;
using System.Text;

namespace graduatio_project.Services;

// Single API instance only: replicas require a shared store with atomic consume.
public sealed class GoogleNonceStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    public GoogleNonceStore(TimeProvider clock) => _clock = clock;

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

    internal bool IsPending(string id, string verifier)
    {
        lock (_gate) return TryGetPending(id, verifier, out _);
    }

    internal bool TryConsume(string id, string nonce, string verifier)
    {
        lock (_gate)
        {
            return TryGetPending(id, verifier, out var entry)
                && FixedEquals(entry.Nonce, nonce) && _entries.Remove(id);
        }
    }

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
    internal static bool FixedEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    private sealed record Entry(string Nonce, string Binding, DateTimeOffset ExpiresAt);
}

public sealed class GoogleFlowCapacityException : Exception;
