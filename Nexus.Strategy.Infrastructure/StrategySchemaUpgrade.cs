using Microsoft.EntityFrameworkCore;

namespace Nexus.StrategyManagement.Infrastructure;

/// <summary>
/// Brings an existing deployment's Strategy schema up to the current model: the position of a strategy among its siblings
/// (SortOrder), so a tree that is saved keeps the order it was arranged in. ModuleSchemaInitializer.EnsureCreatedAsync never adds a column
/// to a table that already exists, so it has to be patched in. A host that runs EnsureCreatedAsync for this module can call this right after
/// it on every start; it is a no-op once the column exists. Hosts that manage the schema by script apply
/// docs/upgrade/2026-10-14-add-strategy-sort-order.sql, which does the same thing.
/// </summary>
public static class StrategySchemaUpgrade
{
    // Existing strategies get 0, which orders them by name exactly as before.
    internal static readonly string[] Statements =
    [
        """
        IF COL_LENGTH(N'strategy.Strategies', N'SortOrder') IS NULL
            ALTER TABLE [strategy].[Strategies] ADD [SortOrder] int NOT NULL CONSTRAINT [DF_Strategies_SortOrder] DEFAULT 0;
        """
    ];

    public static async Task EnsureCurrentAsync(DbContext strategyDbContext, CancellationToken cancellationToken)
    {
        foreach (var statement in Statements)
        {
            await strategyDbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }
    }
}
