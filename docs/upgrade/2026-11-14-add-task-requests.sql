/*
    Task management: requests, request categories and the source a task keeps (phase 5).
    Adds, never drops or rewrites. Safe to run more than once. Hosts that start with the task module applied
    (TaskManagementSchemaPatcher) get the same statements on startup.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-11-14-add-task-requests.sql
*/
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'[task_management].[Tasks]') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'task_management.Tasks', N'SourceType') IS NULL ALTER TABLE [task_management].[Tasks] ADD [SourceType] nvarchar(30) NULL;
    IF COL_LENGTH(N'task_management.Tasks', N'SourceId') IS NULL ALTER TABLE [task_management].[Tasks] ADD [SourceId] uniqueidentifier NULL;
END
IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[RequestCategories]') IS NULL
BEGIN
    CREATE TABLE [task_management].[RequestCategories] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [NormalizedName] nvarchar(100) NOT NULL,
        [RouteToUserId] uniqueidentifier NULL,
        [UrgentRouteToUserId] uniqueidentifier NULL,
        [DefaultPriority] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_RequestCategories] PRIMARY KEY ([Id]));
    CREATE UNIQUE INDEX [IX_RequestCategories_TenantId_NormalizedName] ON [task_management].[RequestCategories] ([TenantId], [NormalizedName]);
END
IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[TaskRequests]') IS NULL
BEGIN
    CREATE TABLE [task_management].[TaskRequests] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CategoryId] uniqueidentifier NULL,
        [Title] nvarchar(200) NOT NULL,
        [Description] nvarchar(4000) NULL,
        [Priority] int NOT NULL,
        [NeededBy] date NULL,
        [RequesterUserId] uniqueidentifier NOT NULL,
        [RoutedToUserId] uniqueidentifier NULL,
        [Status] int NOT NULL,
        [DecisionNote] nvarchar(1000) NULL,
        [DecidedByUserId] uniqueidentifier NULL,
        [DecidedAtUtc] datetimeoffset NULL,
        [ConvertedTaskId] uniqueidentifier NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_TaskRequests] PRIMARY KEY ([Id]));
    CREATE INDEX [IX_TaskRequests_TenantId_Status] ON [task_management].[TaskRequests] ([TenantId], [Status]);
    CREATE INDEX [IX_TaskRequests_TenantId_RequesterUserId] ON [task_management].[TaskRequests] ([TenantId], [RequesterUserId]);
    CREATE INDEX [IX_TaskRequests_TenantId_RoutedToUserId] ON [task_management].[TaskRequests] ([TenantId], [RoutedToUserId]);
END
GO
