using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace OdinVault.Agent;

public sealed record GoogleDrivePairingState(
    string State,
    string TargetName,
    string RedirectUri,
    string? FolderId,
    DateTime ExpiresAtUtc);

public sealed class GoogleDrivePairingStateStore
{
    private readonly ConcurrentDictionary<string, GoogleDrivePairingState> _states = new(StringComparer.Ordinal);

    public GoogleDrivePairingState Create(string targetName, string redirectUri, string? folderId)
    {
        CleanupExpired();
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var pairing = new GoogleDrivePairingState(
            state,
            targetName,
            redirectUri,
            string.IsNullOrWhiteSpace(folderId) ? null : folderId.Trim(),
            DateTime.UtcNow.AddMinutes(10));
        _states[state] = pairing;
        return pairing;
    }

    public bool TryConsume(string state, out GoogleDrivePairingState pairing)
    {
        pairing = default!;
        if (!_states.TryRemove(state, out var stored))
            return false;
        if (stored.ExpiresAtUtc < DateTime.UtcNow)
            return false;
        pairing = stored;
        return true;
    }

    private void CleanupExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in _states)
            if (pair.Value.ExpiresAtUtc < now)
                _states.TryRemove(pair.Key, out _);
    }
}
