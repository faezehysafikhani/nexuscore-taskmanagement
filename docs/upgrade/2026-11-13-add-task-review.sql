/*
    Task management: submit for review (phase 4). Adds the review columns to Tasks.
    Adds, never drops or rewrites. Safe to run more than once. Hosts that start with the task module applied
    (TaskManagementSchemaPatcher) get the same statements on startup.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-11-13-add-task-review.sql
*/
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'[task_management].[Tasks]') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'task_management.Tasks', N'ReviewStatus') IS NULL ALTER TABLE [task_management].[Tasks] ADD [ReviewStatus] int NOT NULL CONSTRAINT [DF_Tasks_ReviewStatus] DEFAULT 0;
    IF COL_LENGTH(N'task_management.Tasks', N'ReviewSubmittedAtUtc') IS NULL ALTER TABLE [task_management].[Tasks] ADD [ReviewSubmittedAtUtc] datetimeoffset NULL;
    IF COL_LENGTH(N'task_management.Tasks', N'ReviewSubmittedByUserId') IS NULL ALTER TABLE [task_management].[Tasks] ADD [ReviewSubmittedByUserId] uniqueidentifier NULL;
    IF COL_LENGTH(N'task_management.Tasks', N'ReviewDecidedAtUtc') IS NULL ALTER TABLE [task_management].[Tasks] ADD [ReviewDecidedAtUtc] datetimeoffset NULL;
    IF COL_LENGTH(N'task_management.Tasks', N'ReviewDecidedByUserId') IS NULL ALTER TABLE [task_management].[Tasks] ADD [ReviewDecidedByUserId] uniqueidentifier NULL;
    IF COL_LENGTH(N'task_management.Tasks', N'ReviewComment') IS NULL ALTER TABLE [task_management].[Tasks] ADD [ReviewComment] nvarchar(1000) NULL;
END
GO
