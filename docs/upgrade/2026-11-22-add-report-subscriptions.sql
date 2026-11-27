/*
    Task management: scheduled digests of a person's tasks, by SMS and email (phase 9c).
    Adds, never drops or rewrites. Safe to run more than once. Hosts that start with the task module applied
    (TaskManagementSchemaPatcher) get the same statements on startup.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-11-22-add-report-subscriptions.sql
*/
SET NOCOUNT ON;
GO

IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[ReportSubscriptions]') IS NULL
BEGIN
    CREATE TABLE [task_management].[ReportSubscriptions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Frequency] int NOT NULL,
        [DayOfWeek] int NULL,
        [DayOfMonth] int NULL,
        [Hour] int NOT NULL,
        [BySms] bit NOT NULL,
        [ByEmail] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [NextRunAtUtc] datetimeoffset NOT NULL,
        [LastSentAtUtc] datetimeoffset NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_ReportSubscriptions] PRIMARY KEY ([Id]));
    CREATE INDEX [IX_ReportSubscriptions_IsActive_NextRunAtUtc] ON [task_management].[ReportSubscriptions] ([IsActive], [NextRunAtUtc]);
    CREATE INDEX [IX_ReportSubscriptions_TenantId_UserId] ON [task_management].[ReportSubscriptions] ([TenantId], [UserId]);
END
GO
