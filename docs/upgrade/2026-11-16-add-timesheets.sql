/*
    Task management: task estimates, time entries, the timer and weekly timesheet submissions (phase 6).
    Adds, never drops or rewrites. Safe to run more than once. Hosts that start with the task module applied
    (TaskManagementSchemaPatcher) get the same statements on startup.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-11-16-add-timesheets.sql
*/
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'[task_management].[Tasks]') IS NOT NULL AND COL_LENGTH(N'task_management.Tasks', N'EstimatedMinutes') IS NULL
    ALTER TABLE [task_management].[Tasks] ADD [EstimatedMinutes] int NULL;
IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[TimeEntries]') IS NULL
BEGIN
    CREATE TABLE [task_management].[TimeEntries] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [TaskId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Date] date NOT NULL,
        [Minutes] int NOT NULL,
        [Note] nvarchar(500) NULL,
        [Source] int NOT NULL,
        [Status] int NOT NULL,
        [SubmissionId] uniqueidentifier NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_TimeEntries] PRIMARY KEY ([Id]));
    CREATE INDEX [IX_TimeEntries_TenantId_UserId_Date] ON [task_management].[TimeEntries] ([TenantId], [UserId], [Date]);
    CREATE INDEX [IX_TimeEntries_TaskId] ON [task_management].[TimeEntries] ([TaskId]);
    CREATE INDEX [IX_TimeEntries_SubmissionId] ON [task_management].[TimeEntries] ([SubmissionId]);
END
IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[TimesheetSubmissions]') IS NULL
BEGIN
    CREATE TABLE [task_management].[TimesheetSubmissions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [WeekStart] date NOT NULL,
        [TotalMinutes] int NOT NULL,
        [SubmittedAtUtc] datetimeoffset NOT NULL,
        [Status] int NOT NULL,
        [DecidedByUserId] uniqueidentifier NULL,
        [DecidedAtUtc] datetimeoffset NULL,
        [DecisionComment] nvarchar(1000) NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_TimesheetSubmissions] PRIMARY KEY ([Id]));
    CREATE INDEX [IX_TimesheetSubmissions_TenantId_UserId_WeekStart] ON [task_management].[TimesheetSubmissions] ([TenantId], [UserId], [WeekStart]);
END
IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[RunningTimers]') IS NULL
    CREATE TABLE [task_management].[RunningTimers] (
        [UserId] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [TaskId] uniqueidentifier NOT NULL,
        [StartedAtUtc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_RunningTimers] PRIMARY KEY ([UserId]));
GO
