using System.Collections.Concurrent;
using OdinVault.Core;

namespace OdinVault.Agent;

public sealed class BackupExecutionCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> locks =
        new(StringComparer.Ordinal);

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        DatabaseEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        var identity = DatabaseIdentity.Create(endpoint);
        var gate = locks.GetOrAdd(identity, static _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken))
            return null;

        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
