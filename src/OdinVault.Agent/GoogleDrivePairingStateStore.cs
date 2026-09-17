using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace OdinVault.Agent;

public sealed record GoogleDrivePairingState(
    string State,
    string TargetName,
    string RedirectUri,
    string? FolderId,
    DateTime ExpiresAtUtc);

public sealed record GoogleDrivePairingStatus(
    string State,
    string Status,
    Guid? StorageTargetId,
    string? Error,
    DateTime ExpiresAtUtc);

public sealed class GoogleDrivePairingStateStore
{
    private readonly ConcurrentDictionary<string, GoogleDrivePairingState> _states = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, GoogleDrivePairingStatus> _statuses = new(StringComparer.Ordinal);

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
        _statuses[state] = new GoogleDrivePairingStatus(state, "pending", null, null, pairing.ExpiresAtUtc);
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

    public GoogleDrivePairingStatus? GetStatus(string state)
    {
        CleanupExpired();
        return _statuses.TryGetValue(state, out var status) ? status : null;
    }

    public void MarkSucceeded(string state, Guid storageTargetId)
    {
        if (_statuses.TryGetValue(state, out var current))
            _statuses[state] = current with { Status = "succeeded", StorageTargetId = storageTargetId, Error = null };
    }

    public void MarkFailed(string state, string error)
    {
        if (_statuses.TryGetValue(state, out var current))
            _statuses[state] = current with { Status = "failed", Error = error };
    }

    private void CleanupExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in _states)
            if (pair.Value.ExpiresAtUtc < now)
                _states.TryRemove(pair.Key, out _);
        foreach (var pair in _statuses)
            if (pair.Value.ExpiresAtUtc.AddMinutes(5) < now)
                _statuses.TryRemove(pair.Key, out _);
    }
}
