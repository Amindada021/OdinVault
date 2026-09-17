using System.Collections.Concurrent;

namespace OdinVault.Agent;

public sealed class BackupExecutionCoordinator
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<IAsyncDisposable?> TryAcquireAsync(Guid databaseId, CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(databaseId, static _ => new SemaphoreSlim(1, 1));
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
