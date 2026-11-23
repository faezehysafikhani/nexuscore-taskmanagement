-- Tasks given to posts for TMPB-API (OrgStructure:AssignTasksToPosts): which posts each
-- task is given to (its creator's, the responsible ones, those with access). Generated from the
-- EF Core migration AddTaskPosts in the NexusCore Nexus.OrgStructure.Infrastructure package
-- (0.1.0.1405072805-mahdi). The API itself gives existing tasks to their people's posts when it
-- starts, so no data is moved here.
-- Idempotent: safe to run again. For a database already upgraded with 1 to 5 (run those first).
-- Not needed when Database:InitializeOrgStructureSchema is true (the default): the API does the same.
--   sqlcmd -S <server> -d TMPB -I -i db/upgrade-6-task-posts.sql
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020183212_AddTaskPosts'
)
BEGIN
    CREATE TABLE [org_structure].[TaskPosts] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [TaskId] uniqueidentifier NOT NULL,
        [PostId] uniqueidentifier NOT NULL,
        [Role] int NOT NULL,
        [HolderUserId] uniqueidentifier NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_TaskPosts] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020183212_AddTaskPosts'
)
BEGIN
    CREATE INDEX [IX_TaskPosts_PostId] ON [org_structure].[TaskPosts] ([PostId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020183212_AddTaskPosts'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TaskPosts_TenantId_TaskId_PostId_Role] ON [org_structure].[TaskPosts] ([TenantId], [TaskId], [PostId], [Role]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020183212_AddTaskPosts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261020183212_AddTaskPosts', N'8.0.24');
END;
GO

COMMIT;
GO

