using System.Security.Cryptography;

namespace graduatio_project.Services;

public sealed record GoogleRegistrationIdentity(string Subject, string Email);

// Bounded, short-lived, single-use bearer tickets, also bound to the initiating tab.
// Remove BEFORE database work: a failed registration requires a fresh Google flow.
public sealed class GoogleRegistrationTicketStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    public GoogleRegistrationTicketStore(TimeProvider clock) => _clock = clock;

    public (string Ticket, DateTimeOffset ExpiresAt) Issue(GoogleRegistrationIdentity identity, string verifier)
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            foreach (var key in _entries.Where(e => e.Value.ExpiresAt <= now).Select(e => e.Key).ToArray())
                _entries.Remove(key);
            if (_entries.Count >= 1024) throw new GoogleFlowCapacityException();
            var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var expiry = now.AddMinutes(5);
            _entries.Add(GoogleNonceStore.Bind(ticket), new Entry(identity, GoogleNonceStore.Bind(verifier), expiry));
            return (ticket, expiry);
        }
    }

    public GoogleRegistrationIdentity? TryTake(string ticket, string verifier)
    {
        lock (_gate)
        {
            var key = GoogleNonceStore.Bind(ticket);
            if (!_entries.TryGetValue(key, out var entry)) return null;
            if (entry.ExpiresAt <= _clock.GetUtcNow()) { _entries.Remove(key); return null; }
            if (!GoogleNonceStore.FixedEquals(entry.Binding, GoogleNonceStore.Bind(verifier))) return null;
            _entries.Remove(key);
            return entry.Identity;
        }
    }
    private sealed record Entry(GoogleRegistrationIdentity Identity, string Binding, DateTimeOffset ExpiresAt);
}
