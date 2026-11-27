/*
    Task management: custom task statuses (phase 3). Adds a TaskStatuses table and Tasks.CustomStatusId.
    Adds, never drops or rewrites. Safe to run more than once. Hosts that start with the task module applied
    (TaskManagementSchemaPatcher) get the same statements on startup.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-11-12-add-custom-task-statuses.sql
*/
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'[task_management].[Tasks]') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'task_management.Tasks', N'CustomStatusId') IS NULL ALTER TABLE [task_management].[Tasks] ADD [CustomStatusId] uniqueidentifier NULL;
END
IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[TaskStatuses]') IS NULL
BEGIN
    CREATE TABLE [task_management].[TaskStatuses] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [NormalizedName] nvarchar(100) NOT NULL,
        [Category] int NOT NULL,
        [Color] nvarchar(30) NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_TaskStatuses] PRIMARY KEY ([Id]));
    CREATE UNIQUE INDEX [IX_TaskStatuses_TenantId_NormalizedName] ON [task_management].[TaskStatuses] ([TenantId], [NormalizedName]);
END
GO

IF OBJECT_ID(N'[task_management].[Tasks]') IS NOT NULL AND COL_LENGTH(N'task_management.Tasks', N'CustomStatusId') IS NOT NULL
    AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Tasks_TenantId_CustomStatusId' AND object_id = OBJECT_ID(N'[task_management].[Tasks]'))
    CREATE INDEX [IX_Tasks_TenantId_CustomStatusId] ON [task_management].[Tasks] ([TenantId], [CustomStatusId]);
GO
