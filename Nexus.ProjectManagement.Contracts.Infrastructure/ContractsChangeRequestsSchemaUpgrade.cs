using Microsoft.EntityFrameworkCore;

namespace Nexus.ProjectManagement.Contracts.Infrastructure;

/// <summary>
/// Brings an existing deployment's project-contracts schema up to the change requests of a project: the ChangeRequests table.
/// ModuleSchemaInitializer.EnsureCreatedAsync never adds a table next to tables that already exist, so it is applied here. A host that runs
/// EnsureCreatedAsync can call this right after it on every start; it is a no-op once done. Hosts that manage the schema by script apply
/// docs/upgrade/2026-10-19-add-change-requests.sql, which holds the same statements in the same order.
/// </summary>
public static class ContractsChangeRequestsSchemaUpgrade
{
    internal static readonly string[] Statements =
    [
        """
        IF OBJECT_ID(N'[project_contracts].[ChangeRequests]', N'U') IS NULL
        CREATE TABLE [project_contracts].[ChangeRequests] (
            [Id] uniqueidentifier NOT NULL,
            [TenantId] uniqueidentifier NOT NULL,
            [ProjectId] uniqueidentifier NOT NULL,
            [Number] int NOT NULL,
            [Title] nvarchar(300) NOT NULL,
            [Description] nvarchar(4000) NULL,
            [Reason] nvarchar(2000) NULL,
            [Type] int NOT NULL,
            [Priority] int NOT NULL,
            [RequestedBy] nvarchar(200) NULL,
            [ContractId] uniqueidentifier NULL,
            [ScopeImpact] nvarchar(4000) NULL,
            [ScheduleImpactDays] int NOT NULL,
            [CostImpact] decimal(19,0) NOT NULL,
            [OtherImpact] nvarchar(2000) NULL,
            [Status] int NOT NULL,
            [SubmittedAtUtc] datetimeoffset NULL,
            [DecidedAtUtc] datetimeoffset NULL,
            [DecisionNote] nvarchar(2000) NULL,
            [ImplementedDate] date NULL,
            [OutcomeNote] nvarchar(2000) NULL,
            [CreatedAtUtc] datetimeoffset NOT NULL,
            [CreatedByUserId] uniqueidentifier NULL,
            [ModifiedAtUtc] datetimeoffset NULL,
            [ModifiedByUserId] uniqueidentifier NULL,
            CONSTRAINT [PK_ChangeRequests] PRIMARY KEY ([Id])
        );
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ChangeRequests_ProjectId_Number' AND object_id = OBJECT_ID(N'[project_contracts].[ChangeRequests]'))
            CREATE UNIQUE INDEX [IX_ChangeRequests_ProjectId_Number] ON [project_contracts].[ChangeRequests] ([ProjectId], [Number]);
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
