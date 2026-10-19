using Microsoft.EntityFrameworkCore;

namespace Nexus.ProjectManagement.Contracts.Infrastructure;

/// <summary>
/// Brings an existing deployment's project-contracts schema up to the milestones and obligations of a contract: the ContractMilestones and
/// ContractObligations tables and the optional ScopeOfWork text on a contract. ModuleSchemaInitializer.EnsureCreatedAsync never adds a table or
/// a column next to tables that already exist, so they are applied here. A host that runs EnsureCreatedAsync can call this right after it on every
/// start; it is a no-op once done. Hosts that manage the schema by script apply docs/upgrade/2026-10-19-add-contract-commitments.sql, which holds
/// the same statements in the same order.
/// </summary>
public static class ContractsCommitmentsSchemaUpgrade
{
    internal static readonly string[] Statements =
    [
        """
        IF OBJECT_ID(N'[project_contracts].[ContractMilestones]', N'U') IS NULL
        CREATE TABLE [project_contracts].[ContractMilestones] (
            [Id] uniqueidentifier NOT NULL,
            [TenantId] uniqueidentifier NOT NULL,
            [ContractId] uniqueidentifier NOT NULL,
            [Title] nvarchar(300) NOT NULL,
            [Description] nvarchar(2000) NULL,
            [DueDate] date NOT NULL,
            [Status] int NOT NULL,
            [CompletedDate] date NULL,
            [CreatedAtUtc] datetimeoffset NOT NULL,
            [CreatedByUserId] uniqueidentifier NULL,
            [ModifiedAtUtc] datetimeoffset NULL,
            [ModifiedByUserId] uniqueidentifier NULL,
            CONSTRAINT [PK_ContractMilestones] PRIMARY KEY ([Id])
        );
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractMilestones_ContractId' AND object_id = OBJECT_ID(N'[project_contracts].[ContractMilestones]'))
            CREATE INDEX [IX_ContractMilestones_ContractId] ON [project_contracts].[ContractMilestones] ([ContractId]);
        """,

        """
        IF OBJECT_ID(N'[project_contracts].[ContractObligations]', N'U') IS NULL
        CREATE TABLE [project_contracts].[ContractObligations] (
            [Id] uniqueidentifier NOT NULL,
            [TenantId] uniqueidentifier NOT NULL,
            [ContractId] uniqueidentifier NOT NULL,
            [Title] nvarchar(300) NOT NULL,
            [Description] nvarchar(2000) NULL,
            [Party] int NOT NULL,
            [DueDate] date NULL,
            [Status] int NOT NULL,
            [FulfilledDate] date NULL,
            [CreatedAtUtc] datetimeoffset NOT NULL,
            [CreatedByUserId] uniqueidentifier NULL,
            [ModifiedAtUtc] datetimeoffset NULL,
            [ModifiedByUserId] uniqueidentifier NULL,
            CONSTRAINT [PK_ContractObligations] PRIMARY KEY ([Id])
        );
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractObligations_ContractId' AND object_id = OBJECT_ID(N'[project_contracts].[ContractObligations]'))
            CREATE INDEX [IX_ContractObligations_ContractId] ON [project_contracts].[ContractObligations] ([ContractId]);
        """,

        """
        IF COL_LENGTH(N'project_contracts.Contracts', N'ScopeOfWork') IS NULL
            ALTER TABLE [project_contracts].[Contracts] ADD [ScopeOfWork] nvarchar(4000) NULL;
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
