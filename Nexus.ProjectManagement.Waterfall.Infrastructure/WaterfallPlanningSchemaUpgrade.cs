using Microsoft.EntityFrameworkCore;

namespace Nexus.ProjectManagement.Waterfall.Infrastructure;

/// <summary>
/// Brings an existing deployment's waterfall schema up to the current model for project planning: an activity's cost,
/// importance, complexity and position, what a saved copy of the schedule records (its kind, the progress and each activity's weight,
/// progress, owner and outline code), the per-project weighting / planning settings table, and the table of activity progress reports.
/// ModuleSchemaInitializer.EnsureCreatedAsync never adds a table or a column to a schema that already exists, so they have to
/// be patched in. A host that runs EnsureCreatedAsync for this module can call this right after it on every start; it is a no-op
/// once everything exists. Hosts that manage the schema by script apply docs/upgrade/2026-10-13-add-waterfall-planning.sql, which
/// does the same thing.
/// </summary>
public static class WaterfallPlanningSchemaUpgrade
{
    // Run one at a time (each is its own batch, as the GO-separated script is), in this order, in
    // one transaction. Kept identical to the DDL EF generates for the model; a test compares them.
    internal static readonly string[] Statements =
    [
        """
        IF COL_LENGTH(N'waterfall.Activities', N'Cost') IS NULL
            ALTER TABLE [waterfall].[Activities] ADD [Cost] decimal(18,2) NULL;
        """,

        """
        IF COL_LENGTH(N'waterfall.Activities', N'Importance') IS NULL
            ALTER TABLE [waterfall].[Activities] ADD [Importance] int NOT NULL CONSTRAINT [DF_Activities_Importance] DEFAULT 1;
        """,

        """
        IF COL_LENGTH(N'waterfall.Activities', N'Complexity') IS NULL
            ALTER TABLE [waterfall].[Activities] ADD [Complexity] int NOT NULL CONSTRAINT [DF_Activities_Complexity] DEFAULT 1;
        """,

        """
        IF COL_LENGTH(N'waterfall.Activities', N'Position') IS NULL
            ALTER TABLE [waterfall].[Activities] ADD [Position] int NOT NULL CONSTRAINT [DF_Activities_Position] DEFAULT 0;
        """,

        """
        IF COL_LENGTH(N'waterfall.ScheduleBaselines', N'Kind') IS NULL
            ALTER TABLE [waterfall].[ScheduleBaselines] ADD [Kind] int NOT NULL CONSTRAINT [DF_ScheduleBaselines_Kind] DEFAULT 0;
        """,

        """
        IF COL_LENGTH(N'waterfall.ScheduleBaselines', N'PlannedProgress') IS NULL
            ALTER TABLE [waterfall].[ScheduleBaselines] ADD [PlannedProgress] decimal(5,2) NOT NULL CONSTRAINT [DF_ScheduleBaselines_PlannedProgress] DEFAULT 0;
        """,

        """
        IF COL_LENGTH(N'waterfall.ScheduleBaselines', N'ActualProgress') IS NULL
            ALTER TABLE [waterfall].[ScheduleBaselines] ADD [ActualProgress] decimal(5,2) NOT NULL CONSTRAINT [DF_ScheduleBaselines_ActualProgress] DEFAULT 0;
        """,

        """
        IF COL_LENGTH(N'waterfall.ScheduleBaselineActivities', N'Weight') IS NULL
            ALTER TABLE [waterfall].[ScheduleBaselineActivities] ADD [Weight] decimal(9,4) NOT NULL CONSTRAINT [DF_ScheduleBaselineActivities_Weight] DEFAULT 0;
        """,

        """
        IF COL_LENGTH(N'waterfall.ScheduleBaselineActivities', N'PlannedProgress') IS NULL
            ALTER TABLE [waterfall].[ScheduleBaselineActivities] ADD [PlannedProgress] decimal(5,2) NOT NULL CONSTRAINT [DF_ScheduleBaselineActivities_PlannedProgress] DEFAULT 0;
        """,

        """
        IF COL_LENGTH(N'waterfall.ScheduleBaselineActivities', N'ActualProgress') IS NULL
            ALTER TABLE [waterfall].[ScheduleBaselineActivities] ADD [ActualProgress] decimal(5,2) NOT NULL CONSTRAINT [DF_ScheduleBaselineActivities_ActualProgress] DEFAULT 0;
        """,

        """
        IF COL_LENGTH(N'waterfall.ScheduleBaselineActivities', N'ResponsibleUserId') IS NULL
            ALTER TABLE [waterfall].[ScheduleBaselineActivities] ADD [ResponsibleUserId] uniqueidentifier NULL;
        """,

        """
        IF COL_LENGTH(N'waterfall.ScheduleBaselineActivities', N'WbsCode') IS NULL
            ALTER TABLE [waterfall].[ScheduleBaselineActivities] ADD [WbsCode] nvarchar(50) NOT NULL CONSTRAINT [DF_ScheduleBaselineActivities_WbsCode] DEFAULT N'';
        """,

        """
        IF OBJECT_ID(N'[waterfall].[WaterfallProjectSettings]', N'U') IS NULL
            CREATE TABLE [waterfall].[WaterfallProjectSettings] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [ProjectId] uniqueidentifier NOT NULL,
                [Weighting] int NOT NULL,
                [TimeFactor] int NOT NULL,
                [CostFactor] int NOT NULL,
                [ComplexityFactor] int NOT NULL,
                [PlannedProgress] int NOT NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_WaterfallProjectSettings] PRIMARY KEY ([Id])
            );
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WaterfallProjectSettings_ProjectId' AND object_id = OBJECT_ID(N'[waterfall].[WaterfallProjectSettings]'))
            CREATE UNIQUE INDEX [IX_WaterfallProjectSettings_ProjectId] ON [waterfall].[WaterfallProjectSettings] ([ProjectId]);
        """,

        """
        IF OBJECT_ID(N'[waterfall].[ActivityProgressSubmissions]', N'U') IS NULL
            CREATE TABLE [waterfall].[ActivityProgressSubmissions] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [ProjectId] uniqueidentifier NOT NULL,
                [ActivityId] uniqueidentifier NOT NULL,
                [SubmittedProgress] decimal(5,2) NOT NULL,
                [ApprovedProgress] decimal(5,2) NULL,
                [IsPartialRejection] bit NOT NULL,
                [Comment] nvarchar(2000) NULL,
                [DocumentName] nvarchar(260) NULL,
                [ApprovalStatus] int NOT NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_ActivityProgressSubmissions] PRIMARY KEY ([Id])
            );
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ActivityProgressSubmissions_ProjectId' AND object_id = OBJECT_ID(N'[waterfall].[ActivityProgressSubmissions]'))
            CREATE INDEX [IX_ActivityProgressSubmissions_ProjectId] ON [waterfall].[ActivityProgressSubmissions] ([ProjectId]);
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ActivityProgressSubmissions_ActivityId' AND object_id = OBJECT_ID(N'[waterfall].[ActivityProgressSubmissions]'))
            CREATE INDEX [IX_ActivityProgressSubmissions_ActivityId] ON [waterfall].[ActivityProgressSubmissions] ([ActivityId]);
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
