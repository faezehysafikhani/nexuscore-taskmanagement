/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) for the Agile planning
    module's sprint features: sprints, story points, an ordered Kanban board and backlog, task
    checklists, and the sprint history the burn-up/burn-down charts and velocity are built from.

    What it does, in order:
      1. adds [agile_planning].[AgileTasks].[StoryPoints] int NULL (existing tasks are not estimated);
      2. adds [agile_planning].[AgileTasks].[Rank] int NOT NULL DEFAULT 0, and - while no task has a
         rank yet - numbers every project's tasks 0, 1, 2... within each status, oldest first, so
         existing boards and backlogs come out in a sensible, stable order;
      3. creates [agile_planning].[Sprints], [SprintEvents] and [ChecklistItems];
      4. creates their indexes, including the unique one that keeps a project's sprint numbers distinct.

    Nothing existing is dropped. The task status UnderReview (value 3) needs no schema change.
    Existing tasks that already carry a SprintNumber keep working exactly as before: a number
    does not need a Sprint row behind it. Define the sprints afterwards through the API.
    Burn charts and velocity only reflect changes made after this upgrade, because earlier
    history was never recorded.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never adds
    a table or a column to a schema that already exists. A host that prefers code can call
    Nexus.ProjectManagement.Agile.Infrastructure.AgileSchemaUpgrade.EnsureCurrentAsync on startup
    instead; it runs the same statements in the same order. Safe to run more than once (the Rank
    numbering is skipped once any task has a rank, so a later run never undoes anyone's reordering).

    Apply this BEFORE deploying the build that contains these features: that build selects the new
    columns, so an AgileTasks table without them fails every query.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-03-add-agile-sprints.sql

    Enum values stored as int:
      Sprints.Status: Planned = 0, Active = 1, Completed = 2.
      SprintEvents.Type: ScopeAdded = 0, ScopeRemoved = 1, Completed = 2, Reopened = 3, CarriedOver = 4.
      AgileTasks.Status: ToDo = 0, InProgress = 1, Done = 2, UnderReview = 3.
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

IF SCHEMA_ID(N'agile_planning') IS NULL EXEC(N'CREATE SCHEMA [agile_planning];');
GO

IF COL_LENGTH(N'agile_planning.AgileTasks', N'StoryPoints') IS NULL
    ALTER TABLE [agile_planning].[AgileTasks] ADD [StoryPoints] int NULL;
GO

IF COL_LENGTH(N'agile_planning.AgileTasks', N'Rank') IS NULL
    ALTER TABLE [agile_planning].[AgileTasks] ADD [Rank] int NOT NULL CONSTRAINT [DF_AgileTasks_Rank] DEFAULT 0;
GO

-- Number the existing tasks only while nobody has a rank yet. A rank other than 0 means the board has
-- been used (or this already ran), and numbering again would undo someone's reordering.
IF NOT EXISTS (SELECT 1 FROM [agile_planning].[AgileTasks] WHERE [Rank] <> 0)
BEGIN
    UPDATE t SET t.[Rank] = ordered.[Position]
    FROM [agile_planning].[AgileTasks] AS t
    JOIN (SELECT [Id], ROW_NUMBER() OVER (PARTITION BY [ProjectId], [Status] ORDER BY [CreatedAtUtc], [Id]) - 1 AS [Position]
          FROM [agile_planning].[AgileTasks]) AS ordered ON ordered.[Id] = t.[Id];
END
GO

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
GO

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
GO

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
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Sprints_ProjectId_Number' AND object_id = OBJECT_ID(N'[agile_planning].[Sprints]'))
    CREATE UNIQUE INDEX [IX_Sprints_ProjectId_Number] ON [agile_planning].[Sprints] ([ProjectId], [Number]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SprintEvents_ProjectId_SprintNumber' AND object_id = OBJECT_ID(N'[agile_planning].[SprintEvents]'))
    CREATE INDEX [IX_SprintEvents_ProjectId_SprintNumber] ON [agile_planning].[SprintEvents] ([ProjectId], [SprintNumber]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ChecklistItems_TaskId' AND object_id = OBJECT_ID(N'[agile_planning].[ChecklistItems]'))
    CREATE INDEX [IX_ChecklistItems_TaskId] ON [agile_planning].[ChecklistItems] ([TaskId]);
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
