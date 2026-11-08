using Microsoft.EntityFrameworkCore;

namespace Nexus.ProjectManagement.Agile.Infrastructure;

/// <summary>
/// Brings an existing deployment's agile-planning schema up to the current model. The module's
/// tables are created by ModuleSchemaInitializer.EnsureCreatedAsync, which never adds a table or a
/// column to a schema that already exists - so what sprints added later (AgileTasks.StoryPoints and
/// Rank, and the Sprints, SprintEvents and ChecklistItems tables) has to be patched in. A host that
/// already runs EnsureCreatedAsync for this module can call this right after it on every start; it
/// is a no-op once everything exists. Hosts that manage the schema by script instead apply
/// docs/upgrade/2026-10-03-add-agile-sprints.sql, which does the same thing.
/// </summary>
public static class AgileSchemaUpgrade
{
    // Run one at a time (each is its own batch, as the GO-separated script is), in this order, in
    // one transaction. Kept identical to docs/upgrade/2026-10-03-add-agile-sprints.sql and to the DDL EF generates
    // for the model; a test compares all three.
    internal static readonly string[] Statements =
    [
        "IF SCHEMA_ID(N'agile_planning') IS NULL EXEC(N'CREATE SCHEMA [agile_planning];');",

        """
        IF COL_LENGTH(N'agile_planning.AgileTasks', N'StoryPoints') IS NULL
            ALTER TABLE [agile_planning].[AgileTasks] ADD [StoryPoints] int NULL;
        """,

        """
        IF COL_LENGTH(N'agile_planning.AgileTasks', N'Rank') IS NULL
            ALTER TABLE [agile_planning].[AgileTasks] ADD [Rank] int NOT NULL CONSTRAINT [DF_AgileTasks_Rank] DEFAULT 0;
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM [agile_planning].[AgileTasks] WHERE [Rank] <> 0)
        BEGIN
            UPDATE t SET t.[Rank] = ordered.[Position]
            FROM [agile_planning].[AgileTasks] AS t
            JOIN (SELECT [Id], ROW_NUMBER() OVER (PARTITION BY [ProjectId], [Status] ORDER BY [CreatedAtUtc], [Id]) - 1 AS [Position]
                  FROM [agile_planning].[AgileTasks]) AS ordered ON ordered.[Id] = t.[Id];
        END
        """,

        """
        IF OBJECT_ID(N'[agile_planning].[Sprints]', N'U') IS NULL
            CREATE TABLE [agile_planning].[Sprints] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [ProjectId] uniqueidentifier NOT NULL,
                [Number] int NOT NULL,
                [Name] nvarchar(200) NOT NULL,
                [Goal] nvarchar(1000) NULL,
                [StartDate] date NULL,
                [EndDate] date NULL,
                [Status] int NOT NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_Sprints] PRIMARY KEY ([Id])
            );
        """,

        """
        IF OBJECT_ID(N'[agile_planning].[SprintEvents]', N'U') IS NULL
            CREATE TABLE [agile_planning].[SprintEvents] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [ProjectId] uniqueidentifier NOT NULL,
                [SprintNumber] int NOT NULL,
                [TaskId] uniqueidentifier NOT NULL,
                [Type] int NOT NULL,
                [Points] int NOT NULL,
                [OccurredAtUtc] datetimeoffset NOT NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_SprintEvents] PRIMARY KEY ([Id])
            );
        """,

        """
        IF OBJECT_ID(N'[agile_planning].[ChecklistItems]', N'U') IS NULL
            CREATE TABLE [agile_planning].[ChecklistItems] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [TaskId] uniqueidentifier NOT NULL,
                [Text] nvarchar(500) NOT NULL,
                [IsDone] bit NOT NULL,
                [Order] int NOT NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_ChecklistItems] PRIMARY KEY ([Id])
            );
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Sprints_ProjectId_Number' AND object_id = OBJECT_ID(N'[agile_planning].[Sprints]'))
            CREATE UNIQUE INDEX [IX_Sprints_ProjectId_Number] ON [agile_planning].[Sprints] ([ProjectId], [Number]);
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SprintEvents_ProjectId_SprintNumber' AND object_id = OBJECT_ID(N'[agile_planning].[SprintEvents]'))
            CREATE INDEX [IX_SprintEvents_ProjectId_SprintNumber] ON [agile_planning].[SprintEvents] ([ProjectId], [SprintNumber]);
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ChecklistItems_TaskId' AND object_id = OBJECT_ID(N'[agile_planning].[ChecklistItems]'))
            CREATE INDEX [IX_ChecklistItems_TaskId] ON [agile_planning].[ChecklistItems] ([TaskId]);
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
