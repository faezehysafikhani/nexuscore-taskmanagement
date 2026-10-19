using Microsoft.EntityFrameworkCore;

namespace Nexus.ProjectManagement.Contracts.Infrastructure;

/// <summary>
/// Brings an existing deployment's project-contracts schema up to addenda that name the change request they carry out: the optional
/// ContractAddenda.ChangeRequestId column and its index. ModuleSchemaInitializer.EnsureCreatedAsync never adds a column to a table that
/// already exists, so it is applied here. A host that runs EnsureCreatedAsync can call this right after it on every start; it is a no-op once
/// done. Hosts that manage the schema by script apply docs/upgrade/2026-10-19-add-addendum-change-request.sql, which holds the same
/// statements in the same order.
/// </summary>
public static class ContractsAddendumChangeSchemaUpgrade
{
    internal static readonly string[] Statements =
    [
        """
        IF COL_LENGTH(N'project_contracts.ContractAddenda', N'ChangeRequestId') IS NULL
            ALTER TABLE [project_contracts].[ContractAddenda] ADD [ChangeRequestId] uniqueidentifier NULL;
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractAddenda_ChangeRequestId' AND object_id = OBJECT_ID(N'[project_contracts].[ContractAddenda]'))
            CREATE INDEX [IX_ContractAddenda_ChangeRequestId] ON [project_contracts].[ContractAddenda] ([ChangeRequestId]);
        """
    ];

    public static async Task EnsureCurrentAsync(DbContext contractsDbContext, CancellationToken cancellationToken)
    {
        await using var transaction = await contractsDbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var statement in Statements)
        {
            await contractsDbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
