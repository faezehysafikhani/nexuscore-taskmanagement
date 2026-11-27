/*
    Task management: automation rules, their execution log and the SLA columns on tasks (phase 7).
    Adds, never drops or rewrites. Safe to run more than once. Hosts that start with the task module applied
    (TaskManagementSchemaPatcher) get the same statements on startup.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-11-17-add-automation-sla.sql
*/
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'[task_management].[Tasks]') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'task_management.Tasks', N'SlaDueAtUtc') IS NULL ALTER TABLE [task_management].[Tasks] ADD [SlaDueAtUtc] datetimeoffset NULL;
    IF COL_LENGTH(N'task_management.Tasks', N'SlaPausedAtUtc') IS NULL ALTER TABLE [task_management].[Tasks] ADD [SlaPausedAtUtc] datetimeoffset NULL;
    IF COL_LENGTH(N'task_management.Tasks', N'SlaBreachedAtUtc') IS NULL ALTER TABLE [task_management].[Tasks] ADD [SlaBreachedAtUtc] datetimeoffset NULL;
END
IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[AutomationRules]') IS NULL
BEGIN
    CREATE TABLE [task_management].[AutomationRules] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Trigger] int NOT NULL,
        [Conditions] nvarchar(max) NOT NULL,
        [Actions] nvarchar(max) NOT NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_AutomationRules] PRIMARY KEY ([Id]));
    CREATE INDEX [IX_AutomationRules_TenantId_Trigger_IsActive] ON [task_management].[AutomationRules] ([TenantId], [Trigger], [IsActive]);
END
IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[AutomationLog]') IS NULL
BEGIN
    CREATE TABLE [task_management].[AutomationLog] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RuleId] uniqueidentifier NOT NULL,
        [RuleName] nvarchar(100) NOT NULL,
        [TaskId] uniqueidentifier NOT NULL,
        [Trigger] int NOT NULL,
        [Outcome] nvarchar(20) NOT NULL,
        [Detail] nvarchar(500) NULL,
        [AtUtc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_AutomationLog] PRIMARY KEY ([Id]));
    CREATE INDEX [IX_AutomationLog_TenantId_AtUtc] ON [task_management].[AutomationLog] ([TenantId], [AtUtc]);
    CREATE INDEX [IX_AutomationLog_TaskId] ON [task_management].[AutomationLog] ([TaskId]);
END
GO
