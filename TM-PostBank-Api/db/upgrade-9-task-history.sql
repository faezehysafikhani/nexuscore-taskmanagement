-- Task history for TMPB-API: who was on each task when, through its posts, with their position
-- then (org_structure.TaskPostPeriods), for «گزارش فعالیت‌ها». Generated from the EF Core migration
-- AddTaskPostPeriods (Nexus.OrgStructure.Infrastructure) in the NexusCore 0.1.0.1405072817-mahdi packages.
-- A new table only. Idempotent: safe to run again. For a database already upgraded with 1 to 8 (run those first).
-- Not needed when Database:InitializeOrgStructureSchema is true (the default): the API does the same.
-- The API fills in who is on each task now at its next start (tasks given to posts before start then).
--   sqlcmd -S <server> -d TMPB -I -i db/upgrade-9-task-history.sql
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026104917_AddTaskPostPeriods'
)
BEGIN
    CREATE TABLE [org_structure].[TaskPostPeriods] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [TaskId] uniqueidentifier NOT NULL,
        [PostId] uniqueidentifier NOT NULL,
        [Role] int NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [PostTitle] nvarchar(200) NOT NULL,
        [PostCode] nvarchar(40) NOT NULL,
        [Place] nvarchar(500) NOT NULL,
        [PositionId] uniqueidentifier NOT NULL,
        [PositionTitle] nvarchar(200) NOT NULL,
        [ActingPostId] uniqueidentifier NULL,
        [StartedAtUtc] datetimeoffset NOT NULL,
        [EndedAtUtc] datetimeoffset NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_TaskPostPeriods] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026104917_AddTaskPostPeriods'
)
BEGIN
    CREATE INDEX [IX_TaskPostPeriods_TenantId_PositionId] ON [org_structure].[TaskPostPeriods] ([TenantId], [PositionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026104917_AddTaskPostPeriods'
)
BEGIN
    CREATE INDEX [IX_TaskPostPeriods_TenantId_TaskId] ON [org_structure].[TaskPostPeriods] ([TenantId], [TaskId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026104917_AddTaskPostPeriods'
)
BEGIN
    CREATE INDEX [IX_TaskPostPeriods_TenantId_UserId] ON [org_structure].[TaskPostPeriods] ([TenantId], [UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026104917_AddTaskPostPeriods'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261026104917_AddTaskPostPeriods', N'8.0.24');
END;
GO

COMMIT;
GO

