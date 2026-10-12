using Microsoft.EntityFrameworkCore;

namespace Nexus.ProjectManagement.Agile.Infrastructure;

/// <summary>
/// Brings an existing deployment's agile-planning schema up to the current model for status-change requests: the table that holds a
/// request to move a task to another column while it waits for approval. ModuleSchemaInitializer.EnsureCreatedAsync never adds a table
/// to a schema that already exists, so it has to be patched in. A host that runs EnsureCreatedAsync for this module can call this right
/// after it on every start; it is a no-op once the table exists. Hosts that manage the schema by script apply
/// docs/upgrade/2026-10-13-add-agile-status-submissions.sql, which does the same thing.
/// </summary>
public static class AgileTaskStatusSchemaUpgrade
{
    // Run one at a time (each is its own batch, as the GO-separated script is), in this order, in
    // one transaction. Kept identical to the DDL EF generates for the model; a test compares them.
    internal static readonly string[] Statements =
    [
        """
        IF OBJECT_ID(N'[agile_planning].[AgileTaskStatusSubmissions]', N'U') IS NULL
            CREATE TABLE [agile_planning].[AgileTaskStatusSubmissions] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [ProjectId] uniqueidentifier NOT NULL,
                [TaskId] uniqueidentifier NOT NULL,
                [PreviousStatus] int NOT NULL,
                [SubmittedStatus] int NOT NULL,
                [Comment] nvarchar(2000) NULL,
                [ApprovalStatus] int NOT NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_AgileTaskStatusSubmissions] PRIMARY KEY ([Id])
            );
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AgileTaskStatusSubmissions_ProjectId' AND object_id = OBJECT_ID(N'[agile_planning].[AgileTaskStatusSubmissions]'))
            CREATE INDEX [IX_AgileTaskStatusSubmissions_ProjectId] ON [agile_planning].[AgileTaskStatusSubmissions] ([ProjectId]);
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AgileTaskStatusSubmissions_TaskId' AND object_id = OBJECT_ID(N'[agile_planning].[AgileTaskStatusSubmissions]'))
            CREATE INDEX [IX_AgileTaskStatusSubmissions_TaskId] ON [agile_planning].[AgileTaskStatusSubmissions] ([TaskId]);
        """
    ];

    public static async Task EnsureCurrentAsync(DbContext agileDbContext, CancellationToken cancellationToken)
    {
        await using var transaction = await agileDbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var statement in Statements)
        {
            await agileDbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
