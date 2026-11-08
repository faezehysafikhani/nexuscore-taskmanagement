using Microsoft.EntityFrameworkCore;

namespace Nexus.ProjectManagement.History.Infrastructure;

/// <summary>
/// Creates the project-history schema on a deployment that predates the History module. A host that runs
/// ModuleSchemaInitializer.EnsureCreatedAsync for ProjectHistoryDbContext already gets it, because the schema is
/// new; this exists for hosts that prefer to call code over running
/// docs/upgrade/2026-10-04-add-project-history.sql, which does the same thing. It is a no-op once the table exists.
/// </summary>
public static class ProjectHistorySchemaUpgrade
{
    // Run one at a time (each is its own batch, as the GO-separated script is), in this order, in
    // one transaction. Kept identical to the DDL EF generates for the model; a test compares them.
    internal static readonly string[] Statements =
    [
        "IF SCHEMA_ID(N'project_history') IS NULL EXEC(N'CREATE SCHEMA [project_history];');",

        """
        IF OBJECT_ID(N'[project_history].[ProjectChanges]', N'U') IS NULL
            CREATE TABLE [project_history].[ProjectChanges] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NULL,
                [ProjectId] uniqueidentifier NOT NULL,
                [EntityName] nvarchar(100) NOT NULL,
                [EntityId] uniqueidentifier NULL,
                [Kind] int NOT NULL,
                [ChangedByUserId] uniqueidentifier NULL,
                [ChangedAtUtc] datetimeoffset NOT NULL,
                [ChangesJson] nvarchar(max) NULL,
                CONSTRAINT [PK_ProjectChanges] PRIMARY KEY ([Id])
            );
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjectChanges_ProjectId_ChangedAtUtc' AND object_id = OBJECT_ID(N'[project_history].[ProjectChanges]'))
            CREATE INDEX [IX_ProjectChanges_ProjectId_ChangedAtUtc] ON [project_history].[ProjectChanges] ([ProjectId], [ChangedAtUtc]);
        """
    ];

    public static async Task EnsureCurrentAsync(DbContext historyDbContext, CancellationToken cancellationToken)
    {
        await using var transaction = await historyDbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var statement in Statements)
        {
            await historyDbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
