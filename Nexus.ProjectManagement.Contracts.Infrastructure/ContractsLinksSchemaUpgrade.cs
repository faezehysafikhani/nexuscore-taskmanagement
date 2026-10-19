using Microsoft.EntityFrameworkCore;

namespace Nexus.ProjectManagement.Contracts.Infrastructure;

/// <summary>
/// Brings an existing deployment's project-contracts schema up to the links of a contract: the ContractLinks table, which points a contract (or one
/// of its milestones or obligations) at a project document or an action. ModuleSchemaInitializer.EnsureCreatedAsync never adds a table next to
/// tables that already exist, so it is applied here. A host that runs EnsureCreatedAsync can call this right after it on every start; it is a
/// no-op once done. Hosts that manage the schema by script apply docs/upgrade/2026-10-19-add-contract-links.sql, which holds the same statements
/// in the same order.
/// </summary>
public static class ContractsLinksSchemaUpgrade
{
    internal static readonly string[] Statements =
    [
        """
        IF OBJECT_ID(N'[project_contracts].[ContractLinks]', N'U') IS NULL
        CREATE TABLE [project_contracts].[ContractLinks] (
            [Id] uniqueidentifier NOT NULL,
            [TenantId] uniqueidentifier NOT NULL,
            [ContractId] uniqueidentifier NOT NULL,
            [SubjectType] int NOT NULL,
            [SubjectId] uniqueidentifier NOT NULL,
            [Kind] int NOT NULL,
            [TargetId] uniqueidentifier NOT NULL,
            [Title] nvarchar(300) NOT NULL,
            [Note] nvarchar(1000) NULL,
            [CreatedAtUtc] datetimeoffset NOT NULL,
            [CreatedByUserId] uniqueidentifier NULL,
            [ModifiedAtUtc] datetimeoffset NULL,
            [ModifiedByUserId] uniqueidentifier NULL,
            CONSTRAINT [PK_ContractLinks] PRIMARY KEY ([Id])
        );
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractLinks_ContractId' AND object_id = OBJECT_ID(N'[project_contracts].[ContractLinks]'))
            CREATE INDEX [IX_ContractLinks_ContractId] ON [project_contracts].[ContractLinks] ([ContractId]);
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractLinks_SubjectId' AND object_id = OBJECT_ID(N'[project_contracts].[ContractLinks]'))
            CREATE INDEX [IX_ContractLinks_SubjectId] ON [project_contracts].[ContractLinks] ([SubjectId]);
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
