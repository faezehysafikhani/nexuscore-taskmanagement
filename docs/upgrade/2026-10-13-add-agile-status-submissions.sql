/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) for agile status-change requests: a team
    member asks for a task to move to another column and an approver confirms it; until then the task stays where it is.

    What it does, in order:
      1. creates [agile_planning].[AgileTaskStatusSubmissions];
      2. creates its two lookup indexes (by project, by task).

    Nothing existing is changed or dropped.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never adds a table to a schema
    that already exists. A host that prefers code can call
    Nexus.ProjectManagement.Agile.Infrastructure.AgileTaskStatusSchemaUpgrade.EnsureCurrentAsync on startup instead; it runs
    the same statements in the same order. Safe to run more than once.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-13-add-agile-status-submissions.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Skipped, without an error, on a database that does not have this module's tables yet: the module creates
-- its whole schema (this change included) the first time the host starts against such a database, and a
-- partial schema made here would stop it from doing so.
IF OBJECT_ID(N'[agile_planning].[AgileTasks]', N'U') IS NULL
BEGIN
    PRINT N'Skipped: Agile planning is not installed in this database ([agile_planning].[AgileTasks] does not exist). Nothing to upgrade.';
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;
GO

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
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AgileTaskStatusSubmissions_ProjectId' AND object_id = OBJECT_ID(N'[agile_planning].[AgileTaskStatusSubmissions]'))
    CREATE INDEX [IX_AgileTaskStatusSubmissions_ProjectId] ON [agile_planning].[AgileTaskStatusSubmissions] ([ProjectId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AgileTaskStatusSubmissions_TaskId' AND object_id = OBJECT_ID(N'[agile_planning].[AgileTaskStatusSubmissions]'))
    CREATE INDEX [IX_AgileTaskStatusSubmissions_TaskId] ON [agile_planning].[AgileTaskStatusSubmissions] ([TaskId]);
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
