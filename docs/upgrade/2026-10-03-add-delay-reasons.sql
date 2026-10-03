/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) with the structured
    "delay reasons" register of the project progress module: what caused a delay, how many days
    and how much money it cost, and the corrective action.

    Adds one new table, [progress].[DelayReasons], and its index. Nothing existing is changed or
    dropped; the free-text DelayReasons column on [progress].[ProgressUpdates] stays as it is.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which only
    creates tables when it finds none - on a database whose progress schema already exists it
    never adds a new table. A host that prefers code can call
    Nexus.ProjectManagement.Progress.Infrastructure.ProgressSchemaUpgrade.EnsureCurrentAsync on
    startup instead; it runs the same statements. Safe to run more than once.

    Apply this BEFORE deploying the build that contains the DelayReasons endpoints.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-03-add-delay-reasons.sql

    RootCause holds: Funding = 0, Procurement = 1, Permits = 2, HumanResources = 3, Other = 4.
    ApprovalStatus holds the shared NexusCore approval status values.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF SCHEMA_ID(N'progress') IS NULL EXEC(N'CREATE SCHEMA [progress];');

IF OBJECT_ID(N'[progress].[DelayReasons]', N'U') IS NULL
    CREATE TABLE [progress].[DelayReasons] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [RegisterDate] date NOT NULL,
        [RootCause] int NOT NULL,
        [Description] nvarchar(2000) NOT NULL,
        [TimeImpactDays] int NULL,
        [CostImpact] decimal(18,0) NULL,
        [CorrectiveAction] nvarchar(2000) NULL,
        [ApprovalStatus] int NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_DelayReasons] PRIMARY KEY ([Id])
    );

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DelayReasons_ProjectId' AND object_id = OBJECT_ID(N'[progress].[DelayReasons]'))
    CREATE INDEX [IX_DelayReasons_ProjectId] ON [progress].[DelayReasons] ([ProjectId]);

COMMIT TRANSACTION;
GO
