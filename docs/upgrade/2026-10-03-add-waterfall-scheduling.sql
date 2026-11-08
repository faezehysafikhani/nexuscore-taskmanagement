/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) for the Waterfall
    planning module's scheduling features: milestones, activity dependencies (with lags), schedule
    baselines, and progress snapshots for the S-curve.

    What it does, in order:
      1. adds [waterfall].[Activities].[IsMilestone] bit NOT NULL DEFAULT 0 (existing activities
         are ordinary activities, as they always were);
      2. creates [waterfall].[ActivityDependencies];
      3. creates [waterfall].[ScheduleBaselines] and [waterfall].[ScheduleBaselineActivities];
      4. creates [waterfall].[ProgressSnapshots];
      5. creates the indexes, including the unique ones that keep a pair of activities to one link,
         a project's baseline numbers distinct, and a project to one snapshot per date.

    Nothing existing is changed or dropped; the new tables start empty. MS Project import/export,
    the schedule calculation and the calendar integration need no schema.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never adds
    a table or a column to a schema that already exists. A host that prefers code can call
    Nexus.ProjectManagement.Waterfall.Infrastructure.WaterfallSchemaUpgrade.EnsureCurrentAsync on
    startup instead; it runs the same statements in the same order. Safe to run more than once.

    Apply this BEFORE deploying the build that contains these features: that build selects the new
    IsMilestone column, so an Activities table without it fails every query.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-03-add-waterfall-scheduling.sql

    Enum values stored as int:
      ActivityDependencies.Type: FinishToStart = 0, StartToStart = 1, FinishToFinish = 2, StartToFinish = 3.
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

IF SCHEMA_ID(N'waterfall') IS NULL EXEC(N'CREATE SCHEMA [waterfall];');
GO

IF COL_LENGTH(N'waterfall.Activities', N'IsMilestone') IS NULL
    ALTER TABLE [waterfall].[Activities] ADD [IsMilestone] bit NOT NULL CONSTRAINT [DF_Activities_IsMilestone] DEFAULT CAST(0 AS bit);
GO

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
GO

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
GO

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
GO

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
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ActivityDependencies_PredecessorActivityId_SuccessorActivityId' AND object_id = OBJECT_ID(N'[waterfall].[ActivityDependencies]'))
    CREATE UNIQUE INDEX [IX_ActivityDependencies_PredecessorActivityId_SuccessorActivityId] ON [waterfall].[ActivityDependencies] ([PredecessorActivityId], [SuccessorActivityId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ActivityDependencies_ProjectId' AND object_id = OBJECT_ID(N'[waterfall].[ActivityDependencies]'))
    CREATE INDEX [IX_ActivityDependencies_ProjectId] ON [waterfall].[ActivityDependencies] ([ProjectId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ActivityDependencies_SuccessorActivityId' AND object_id = OBJECT_ID(N'[waterfall].[ActivityDependencies]'))
    CREATE INDEX [IX_ActivityDependencies_SuccessorActivityId] ON [waterfall].[ActivityDependencies] ([SuccessorActivityId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ScheduleBaselines_ProjectId_Number' AND object_id = OBJECT_ID(N'[waterfall].[ScheduleBaselines]'))
    CREATE UNIQUE INDEX [IX_ScheduleBaselines_ProjectId_Number] ON [waterfall].[ScheduleBaselines] ([ProjectId], [Number]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ScheduleBaselineActivities_BaselineId' AND object_id = OBJECT_ID(N'[waterfall].[ScheduleBaselineActivities]'))
    CREATE INDEX [IX_ScheduleBaselineActivities_BaselineId] ON [waterfall].[ScheduleBaselineActivities] ([BaselineId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProgressSnapshots_ProjectId_SnapshotDate' AND object_id = OBJECT_ID(N'[waterfall].[ProgressSnapshots]'))
    CREATE UNIQUE INDEX [IX_ProgressSnapshots_ProjectId_SnapshotDate] ON [waterfall].[ProgressSnapshots] ([ProjectId], [SnapshotDate]);
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
