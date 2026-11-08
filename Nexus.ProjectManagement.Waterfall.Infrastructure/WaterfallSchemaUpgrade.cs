using Microsoft.EntityFrameworkCore;

namespace Nexus.ProjectManagement.Waterfall.Infrastructure;

/// <summary>
/// Brings an existing deployment's waterfall schema up to the current model. The module's tables
/// are created by ModuleSchemaInitializer.EnsureCreatedAsync, which never adds a table or a column
/// to a schema that already exists - so what scheduling added later (Activities.IsMilestone and
/// the dependency, baseline and progress-snapshot tables) has to be patched in. A host that
/// already runs EnsureCreatedAsync for this module can call this right after it on every start; it
/// is a no-op once everything exists. Hosts that manage the schema by script instead apply
/// docs/upgrade/2026-10-03-add-waterfall-scheduling.sql, which does the same thing.
/// </summary>
public static class WaterfallSchemaUpgrade
{
    // Run one at a time (each is its own batch, as the GO-separated script is), in this order, in
    // one transaction. Kept identical to the script and to the DDL EF generates for the model; a
    // test compares all three.
    internal static readonly string[] Statements =
    [
        "IF SCHEMA_ID(N'waterfall') IS NULL EXEC(N'CREATE SCHEMA [waterfall];');",

        """
        IF COL_LENGTH(N'waterfall.Activities', N'IsMilestone') IS NULL
            ALTER TABLE [waterfall].[Activities] ADD [IsMilestone] bit NOT NULL CONSTRAINT [DF_Activities_IsMilestone] DEFAULT CAST(0 AS bit);
        """,

        """
        IF OBJECT_ID(N'[waterfall].[ActivityDependencies]', N'U') IS NULL
            CREATE TABLE [waterfall].[ActivityDependencies] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [ProjectId] uniqueidentifier NOT NULL,
                [PredecessorActivityId] uniqueidentifier NOT NULL,
                [SuccessorActivityId] uniqueidentifier NOT NULL,
                [Type] int NOT NULL,
                [LagDays] int NOT NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_ActivityDependencies] PRIMARY KEY ([Id])
            );
        """,

        """
        IF OBJECT_ID(N'[waterfall].[ScheduleBaselines]', N'U') IS NULL
            CREATE TABLE [waterfall].[ScheduleBaselines] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [ProjectId] uniqueidentifier NOT NULL,
                [Number] int NOT NULL,
                [Name] nvarchar(200) NOT NULL,
                [Note] nvarchar(2000) NULL,
                [ProjectStart] date NOT NULL,
                [ProjectFinish] date NOT NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_ScheduleBaselines] PRIMARY KEY ([Id])
            );
        """,

        """
        IF OBJECT_ID(N'[waterfall].[ScheduleBaselineActivities]', N'U') IS NULL
            CREATE TABLE [waterfall].[ScheduleBaselineActivities] (
                [Id] uniqueidentifier NOT NULL,
                [BaselineId] uniqueidentifier NOT NULL,
                [ActivityId] uniqueidentifier NOT NULL,
                [ParentActivityId] uniqueidentifier NULL,
                [Name] nvarchar(200) NOT NULL,
                [IsSummary] bit NOT NULL,
                [IsMilestone] bit NOT NULL,
                [StartDate] date NOT NULL,
                [EndDate] date NOT NULL,
                [DurationDays] int NOT NULL,
                CONSTRAINT [PK_ScheduleBaselineActivities] PRIMARY KEY ([Id])
            );
        """,

        """
        IF OBJECT_ID(N'[waterfall].[ProgressSnapshots]', N'U') IS NULL
            CREATE TABLE [waterfall].[ProgressSnapshots] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [ProjectId] uniqueidentifier NOT NULL,
                [SnapshotDate] date NOT NULL,
                [PlannedProgress] decimal(5,2) NOT NULL,
                [ActualProgress] decimal(5,2) NOT NULL,
                [Note] nvarchar(1000) NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_ProgressSnapshots] PRIMARY KEY ([Id])
            );
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ActivityDependencies_PredecessorActivityId_SuccessorActivityId' AND object_id = OBJECT_ID(N'[waterfall].[ActivityDependencies]'))
            CREATE UNIQUE INDEX [IX_ActivityDependencies_PredecessorActivityId_SuccessorActivityId] ON [waterfall].[ActivityDependencies] ([PredecessorActivityId], [SuccessorActivityId]);
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ActivityDependencies_ProjectId' AND object_id = OBJECT_ID(N'[waterfall].[ActivityDependencies]'))
            CREATE INDEX [IX_ActivityDependencies_ProjectId] ON [waterfall].[ActivityDependencies] ([ProjectId]);
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ActivityDependencies_SuccessorActivityId' AND object_id = OBJECT_ID(N'[waterfall].[ActivityDependencies]'))
            CREATE INDEX [IX_ActivityDependencies_SuccessorActivityId] ON [waterfall].[ActivityDependencies] ([SuccessorActivityId]);
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ScheduleBaselines_ProjectId_Number' AND object_id = OBJECT_ID(N'[waterfall].[ScheduleBaselines]'))
            CREATE UNIQUE INDEX [IX_ScheduleBaselines_ProjectId_Number] ON [waterfall].[ScheduleBaselines] ([ProjectId], [Number]);
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ScheduleBaselineActivities_BaselineId' AND object_id = OBJECT_ID(N'[waterfall].[ScheduleBaselineActivities]'))
            CREATE INDEX [IX_ScheduleBaselineActivities_BaselineId] ON [waterfall].[ScheduleBaselineActivities] ([BaselineId]);
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProgressSnapshots_ProjectId_SnapshotDate' AND object_id = OBJECT_ID(N'[waterfall].[ProgressSnapshots]'))
            CREATE UNIQUE INDEX [IX_ProgressSnapshots_ProjectId_SnapshotDate] ON [waterfall].[ProgressSnapshots] ([ProjectId], [SnapshotDate]);
        """
    ];

    public static async Task EnsureCurrentAsync(DbContext waterfallDbContext, CancellationToken cancellationToken)
    {
        await using var transaction = await waterfallDbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var statement in Statements)
        {
            await waterfallDbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
