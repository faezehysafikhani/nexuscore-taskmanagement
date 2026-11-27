/*
    Task management: notification outbox, edit-conflict row version and the task archive (phase 2).
    Adds, never drops or rewrites. Safe to run more than once. Hosts that start with the task module applied
    (TaskManagementSchemaPatcher) get the same statements on startup, so this script is for sites that manage the schema by script.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-11-10-add-task-outbox-archive-concurrency.sql
*/
SET NOCOUNT ON;
GO

IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[NotificationOutbox]') IS NULL
BEGIN
    CREATE TABLE [task_management].[NotificationOutbox] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [TaskId] uniqueidentifier NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Description] nvarchar(4000) NULL,
        [DueDate] date NOT NULL,
        [DueAtText] nvarchar(100) NULL,
        [RecipientUserId] uniqueidentifier NOT NULL,
        [DedupeKey] nvarchar(120) NOT NULL,
        [Attempts] int NOT NULL,
        [NextAttemptAtUtc] datetimeoffset NOT NULL,
        [DeliveredAtUtc] datetimeoffset NULL,
        [LastError] nvarchar(500) NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_NotificationOutbox] PRIMARY KEY ([Id]));
    CREATE UNIQUE INDEX [IX_NotificationOutbox_DedupeKey] ON [task_management].[NotificationOutbox] ([DedupeKey]);
    CREATE INDEX [IX_NotificationOutbox_DeliveredAtUtc_NextAttemptAtUtc] ON [task_management].[NotificationOutbox] ([DeliveredAtUtc], [NextAttemptAtUtc]);
END
GO

IF OBJECT_ID(N'[task_management].[Tasks]') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'task_management.Tasks', N'RowVersion') IS NULL ALTER TABLE [task_management].[Tasks] ADD [RowVersion] rowversion;
    IF COL_LENGTH(N'task_management.Tasks', N'ArchivedAtUtc') IS NULL ALTER TABLE [task_management].[Tasks] ADD [ArchivedAtUtc] datetimeoffset NULL;
    IF COL_LENGTH(N'task_management.Tasks', N'ArchivedByUserId') IS NULL ALTER TABLE [task_management].[Tasks] ADD [ArchivedByUserId] uniqueidentifier NULL;
END
GO

IF OBJECT_ID(N'[task_management].[Tasks]') IS NOT NULL AND COL_LENGTH(N'task_management.Tasks', N'ArchivedAtUtc') IS NOT NULL
    AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Tasks_TenantId_ArchivedAtUtc' AND object_id = OBJECT_ID(N'[task_management].[Tasks]'))
    CREATE INDEX [IX_Tasks_TenantId_ArchivedAtUtc] ON [task_management].[Tasks] ([TenantId], [ArchivedAtUtc]);
GO
