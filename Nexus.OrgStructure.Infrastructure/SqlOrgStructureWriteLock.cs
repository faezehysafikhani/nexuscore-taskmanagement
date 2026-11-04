using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nexus.OrgStructure.Application;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.OrgStructure.Infrastructure;

/// <summary>
/// On SQL Server: the organization's turn is an exclusive application lock (sp_getapplock) held by
/// a transaction of this module's context, so it covers every server of the product. This module's
/// changes are saved in that transaction, then the units' (their own context); if the units cannot
/// be saved (someone changed one: its RowVersion), this module's changes are rolled back too.
/// Other databases (the tests): the in-process lock, with the same saving order.
/// </summary>
public sealed class SqlOrgStructureWriteLock(OrgStructureDbContext dbContext) : IOrgStructureWriteLock
{
    private const int WaitMilliseconds = 15000;

    public async Task<IOrgStructureWrite?> BeginAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsSqlServer())
        {
            return await InProcessOrgStructureWriteLock.EnterAsync(tenantId, TimeSpan.FromMilliseconds(WaitMilliseconds), cancellationToken) is { } turn
                ? new Write(null, turn)
                : null;
        }

        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var granted = await dbContext.Database
                .SqlQueryRaw<int>(
                    "DECLARE @result int; EXEC @result = sp_getapplock @Resource = {0}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {1}; SELECT @result AS [Value];",
                    $"org-structure:{tenantId:N}", WaitMilliseconds)
                .ToListAsync(cancellationToken);
            if (granted.Count == 1 && granted[0] >= 0)
            {
                return new Write(transaction, null);
            }
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }

        await transaction.DisposeAsync();
        return null;
    }

    private sealed class Write(IDbContextTransaction? transaction, IAsyncDisposable? turn) : IOrgStructureWrite
    {
        public async Task<bool> SaveAsync(IUnitOfWork structure, IUnitOfWork units, CancellationToken cancellationToken)
        {
            try
            {
                await structure.SaveChangesAsync(cancellationToken);
                await units.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            // Not committed: rolled back (and the lock released with it).
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }

            if (turn is not null)
            {
                await turn.DisposeAsync();
            }
        }
    }
}
