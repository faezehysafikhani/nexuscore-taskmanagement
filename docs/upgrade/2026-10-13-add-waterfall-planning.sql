/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) for project planning in the waterfall
    module: an activity's cost, importance, complexity and position, and the per-project settings that say how a project
    weighs its activities (manual / by time / by cost / by complexity / combined) and how it reads planned progress, and
    the table that holds activity progress reports awaiting approval.

    What it does, in order:
      1. adds the columns [waterfall].[Activities].[Cost] (nullable), [Importance] and [Complexity] (default 1 = medium)
         and [Position] (default 0 = "as created");
      1b. adds what a saved copy of the schedule now records: [ScheduleBaselines].[Kind], [PlannedProgress], [ActualProgress] and
         [ScheduleBaselineActivities].[Weight], [PlannedProgress], [ActualProgress], [ResponsibleUserId], [WbsCode] (existing copies read as
         plain baselines with no progress);
      2. creates [waterfall].[WaterfallProjectSettings] (one optional row per project; a project without a row keeps the
         original behaviour: typed weights, stored planned progress);
      3. creates its unique index on ProjectId;
      4. creates [waterfall].[ActivityProgressSubmissions] (a team member's report that an activity has reached some percent
         complete, waiting for an approver) and its two lookup indexes.

    Existing activities keep working exactly as before. Nothing is changed or dropped.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never adds a table or a column
    to a schema that already exists. A host that prefers code can call
    Nexus.ProjectManagement.Waterfall.Infrastructure.WaterfallPlanningSchemaUpgrade.EnsureCurrentAsync on startup instead; it
    runs the same statements in the same order. Safe to run more than once.

    Apply this BEFORE deploying the build that contains these features: that build selects the new columns, so an
    Activities table without them fails every activity query.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-13-add-waterfall-planning.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Skipped, without an error, on a database that does not have this module's tables yet: the module creates
-- its whole schema (this change included) the first time the host starts against such a database, and a
-- partial schema made here would stop it from doing so.
IF OBJECT_ID(N'[waterfall].[Activities]', N'U') IS NULL
BEGIN
    PRINT N'Skipped: Waterfall planning is not installed in this database ([waterfall].[Activities] does not exist). Nothing to upgrade.';
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;
GO

IF COL_LENGTH(N'waterfall.Activities', N'Cost') IS NULL
    ALTER TABLE [waterfall].[Activities] ADD [Cost] decimal(18,2) NULL;
GO

IF COL_LENGTH(N'waterfall.Activities', N'Importance') IS NULL
    ALTER TABLE [waterfall].[Activities] ADD [Importance] int NOT NULL CONSTRAINT [DF_Activities_Importance] DEFAULT 1;
GO

IF COL_LENGTH(N'waterfall.Activities', N'Complexity') IS NULL
    ALTER TABLE [waterfall].[Activities] ADD [Complexity] int NOT NULL CONSTRAINT [DF_Activities_Complexity] DEFAULT 1;
GO

IF COL_LENGTH(N'waterfall.Activities', N'Position') IS NULL
    ALTER TABLE [waterfall].[Activities] ADD [Position] int NOT NULL CONSTRAINT [DF_Activities_Position] DEFAULT 0;
GO

IF COL_LENGTH(N'waterfall.ScheduleBaselines', N'Kind') IS NULL
    ALTER TABLE [waterfall].[ScheduleBaselines] ADD [Kind] int NOT NULL CONSTRAINT [DF_ScheduleBaselines_Kind] DEFAULT 0;
GO

IF COL_LENGTH(N'waterfall.ScheduleBaselines', N'PlannedProgress') IS NULL
    ALTER TABLE [waterfall].[ScheduleBaselines] ADD [PlannedProgress] decimal(5,2) NOT NULL CONSTRAINT [DF_ScheduleBaselines_PlannedProgress] DEFAULT 0;
GO

IF COL_LENGTH(N'waterfall.ScheduleBaselines', N'ActualProgress') IS NULL
    ALTER TABLE [waterfall].[ScheduleBaselines] ADD [ActualProgress] decimal(5,2) NOT NULL CONSTRAINT [DF_ScheduleBaselines_ActualProgress] DEFAULT 0;
GO

IF COL_LENGTH(N'waterfall.ScheduleBaselineActivities', N'Weight') IS NULL
    ALTER TABLE [waterfall].[ScheduleBaselineActivities] ADD [Weight] decimal(9,4) NOT NULL CONSTRAINT [DF_ScheduleBaselineActivities_Weight] DEFAULT 0;
GO

IF COL_LENGTH(N'waterfall.ScheduleBaselineActivities', N'PlannedProgress') IS NULL
    ALTER TABLE [waterfall].[ScheduleBaselineActivities] ADD [PlannedProgress] decimal(5,2) NOT NULL CONSTRAINT [DF_ScheduleBaselineActivities_PlannedProgress] DEFAULT 0;
GO

IF COL_LENGTH(N'waterfall.ScheduleBaselineActivities', N'ActualProgress') IS NULL
    ALTER TABLE [waterfall].[ScheduleBaselineActivities] ADD [ActualProgress] decimal(5,2) NOT NULL CONSTRAINT [DF_ScheduleBaselineActivities_ActualProgress] DEFAULT 0;
GO

IF COL_LENGTH(N'waterfall.ScheduleBaselineActivities', N'ResponsibleUserId') IS NULL
    ALTER TABLE [waterfall].[ScheduleBaselineActivities] ADD [ResponsibleUserId] uniqueidentifier NULL;
GO

IF COL_LENGTH(N'waterfall.ScheduleBaselineActivities', N'WbsCode') IS NULL
    ALTER TABLE [waterfall].[ScheduleBaselineActivities] ADD [WbsCode] nvarchar(50) NOT NULL CONSTRAINT [DF_ScheduleBaselineActivities_WbsCode] DEFAULT N'';
GO

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
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WaterfallProjectSettings_ProjectId' AND object_id = OBJECT_ID(N'[waterfall].[WaterfallProjectSettings]'))
    CREATE UNIQUE INDEX [IX_WaterfallProjectSettings_ProjectId] ON [waterfall].[WaterfallProjectSettings] ([ProjectId]);
GO

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
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ActivityProgressSubmissions_ProjectId' AND object_id = OBJECT_ID(N'[waterfall].[ActivityProgressSubmissions]'))
    CREATE INDEX [IX_ActivityProgressSubmissions_ProjectId] ON [waterfall].[ActivityProgressSubmissions] ([ProjectId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ActivityProgressSubmissions_ActivityId' AND object_id = OBJECT_ID(N'[waterfall].[ActivityProgressSubmissions]'))
    CREATE INDEX [IX_ActivityProgressSubmissions_ActivityId] ON [waterfall].[ActivityProgressSubmissions] ([ActivityId]);
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
