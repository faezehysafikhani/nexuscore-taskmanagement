/*
    Task management: dependencies, blocker, acceptance criteria, WIP limit and templates (phase 3c).
    Adds, never drops or rewrites. Safe to run more than once. Hosts that start with the task module applied
    (TaskManagementSchemaPatcher) get the same statements on startup.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-11-15-add-task-planning.sql
*/
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'[task_management].[Tasks]') IS NOT NULL AND COL_LENGTH(N'task_management.Tasks', N'BlockedReason') IS NULL
    ALTER TABLE [task_management].[Tasks] ADD [BlockedReason] nvarchar(300) NULL;
IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[TaskDependencies]') IS NULL
BEGIN
    CREATE TABLE [task_management].[TaskDependencies] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [TaskId] uniqueidentifier NOT NULL,
        [BlockedByTaskId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_TaskDependencies] PRIMARY KEY ([Id]));
    CREATE UNIQUE INDEX [IX_TaskDependencies_TaskId_BlockedByTaskId] ON [task_management].[TaskDependencies] ([TaskId], [BlockedByTaskId]);
    CREATE INDEX [IX_TaskDependencies_BlockedByTaskId] ON [task_management].[TaskDependencies] ([BlockedByTaskId]);
END
IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[TaskCriteria]') IS NULL
BEGIN
    CREATE TABLE [task_management].[TaskCriteria] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [TaskId] uniqueidentifier NOT NULL,
        [Text] nvarchar(300) NOT NULL,
        [IsMet] bit NOT NULL,
        [MetByUserId] uniqueidentifier NULL,
        [MetAtUtc] datetimeoffset NULL,
        [SortOrder] int NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_TaskCriteria] PRIMARY KEY ([Id]));
    CREATE INDEX [IX_TaskCriteria_TaskId] ON [task_management].[TaskCriteria] ([TaskId]);
END
IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[TaskWipSettings]') IS NULL
    CREATE TABLE [task_management].[TaskWipSettings] (
        [TenantId] uniqueidentifier NOT NULL,
        [LimitPerUser] int NULL,
        CONSTRAINT [PK_TaskWipSettings] PRIMARY KEY ([TenantId]));
IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[TaskTemplates]') IS NULL
BEGIN
    CREATE TABLE [task_management].[TaskTemplates] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [NormalizedName] nvarchar(100) NOT NULL,
        [IsProject] bit NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_TaskTemplates] PRIMARY KEY ([Id]));
    CREATE UNIQUE INDEX [IX_TaskTemplates_TenantId_NormalizedName] ON [task_management].[TaskTemplates] ([TenantId], [NormalizedName]);
END
GO
