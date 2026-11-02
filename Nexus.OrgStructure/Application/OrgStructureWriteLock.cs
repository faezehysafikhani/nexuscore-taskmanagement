using System.Collections.Concurrent;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// One change to an organization's structure at a time: a node is moved (or made, or its type
/// rules changed) only after the previous change is saved, and the change is checked (loops, rules)
/// against the data as it is once the turn has come. So two moves at the same moment cannot
/// together make a loop that neither makes alone.
/// </summary>
public interface IOrgStructureWriteLock
{
    /// <summary>Waits for the organization's turn; null when it did not come in time (someone else is changing it).</summary>
    Task<IOrgStructureWrite?> BeginAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>One turn: save once (or not at all), then dispose to let the next change in.</summary>
public interface IOrgStructureWrite : IAsyncDisposable
{
    /// <summary>
    /// Saves this module's changes and the units' (Nexus.Organization) together. False, with
    /// nothing saved: a unit or post was changed by someone else since it was read.
    /// </summary>
    Task<bool> SaveAsync(IUnitOfWork structure, IUnitOfWork units, CancellationToken cancellationToken);
}

/// <summary>
/// Within this process only (one server, and the tests). The infrastructure registers the
/// database-wide lock for SQL Server.
/// </summary>
public sealed class InProcessOrgStructureWriteLock : IOrgStructureWriteLock
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();

    public static async Task<IAsyncDisposable?> EnterAsync(Guid tenantId, TimeSpan wait, CancellationToken cancellationToken)
    {
        var gate = Gates.GetOrAdd(tenantId, _ => new SemaphoreSlim(1, 1));
        return await gate.WaitAsync(wait, cancellationToken) ? new Release(gate) : null;
    }

    public async Task<IOrgStructureWrite?> BeginAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await EnterAsync(tenantId, TimeSpan.FromSeconds(15), cancellationToken) is { } turn ? new Write(turn) : null;

    private sealed class Write(IAsyncDisposable turn) : IOrgStructureWrite
    {
        public async Task<bool> SaveAsync(IUnitOfWork structure, IUnitOfWork units, CancellationToken cancellationToken)
        {
            await structure.SaveChangesAsync(cancellationToken);
            await units.SaveChangesAsync(cancellationToken);
            return true;
        }

        public ValueTask DisposeAsync() => turn.DisposeAsync();
    }

    private sealed class Release(SemaphoreSlim gate) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
