using Microsoft.EntityFrameworkCore;

namespace Nexus.Workflow.Infrastructure;

/// <summary>
/// Brings an existing deployment's workflow schema up to the current model: the delegations table
/// and the OnBehalfOfUserId column on decisions. ModuleSchemaInitializer.EnsureCreatedAsync never adds a
/// table or a column to a schema that already exists, so they have to be patched in. A host that runs
/// EnsureCreatedAsync for this module can call this right after it on every start; it is a no-op once
/// everything exists. Hosts that manage the schema by script apply
/// docs/upgrade/2026-10-04-add-workflow-delegation.sql, which does the same thing.
/// </summary>
public static class WorkflowSchemaUpgrade
{
    // Run one at a time (each is its own batch, as the GO-separated script is), in this order, in
    // one transaction. Kept identical to the DDL EF generates for the model; a test compares them.
    internal static readonly string[] Statements =
    [
        """
        IF OBJECT_ID(N'[workflow].[WorkflowDelegations]', N'U') IS NULL
            CREATE TABLE [workflow].[WorkflowDelegations] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [DelegatorUserId] uniqueidentifier NOT NULL,
                [DelegateUserId] uniqueidentifier NOT NULL,
                [StartDate] date NOT NULL,
                [EndDate] date NOT NULL,
                [SubjectType] nvarchar(80) NULL,
                [Reason] nvarchar(500) NULL,
                [IsRevoked] bit NOT NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_WorkflowDelegations] PRIMARY KEY ([Id])
            );
        """,

        """
        IF COL_LENGTH(N'workflow.WorkflowDecisions', N'OnBehalfOfUserId') IS NULL
            ALTER TABLE [workflow].[WorkflowDecisions] ADD [OnBehalfOfUserId] uniqueidentifier NULL;
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowDelegations_TenantId_DelegateUserId' AND object_id = OBJECT_ID(N'[workflow].[WorkflowDelegations]'))
            CREATE INDEX [IX_WorkflowDelegations_TenantId_DelegateUserId] ON [workflow].[WorkflowDelegations] ([TenantId], [DelegateUserId]);
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowDelegations_TenantId_DelegatorUserId' AND object_id = OBJECT_ID(N'[workflow].[WorkflowDelegations]'))
            CREATE INDEX [IX_WorkflowDelegations_TenantId_DelegatorUserId] ON [workflow].[WorkflowDelegations] ([TenantId], [DelegatorUserId]);
        """
    ];

    public static async Task EnsureCurrentAsync(DbContext workflowDbContext, CancellationToken cancellationToken)
    {
        await using var transaction = await workflowDbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var statement in Statements)
        {
            await workflowDbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
