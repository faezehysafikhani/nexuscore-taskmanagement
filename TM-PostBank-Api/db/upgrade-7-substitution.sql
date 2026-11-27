-- Substitution inbox for TMPB-API (tasks given to posts): which post stands in for a vacant
-- one (org_structure.PostActings), and in task_management: stand-ins on tasks, their comments, and
-- tasks owned through a post. Generated from the EF Core migrations AddPostActings
-- (Nexus.OrgStructure.Infrastructure) and AddPostOwnershipAndActing (Nexus.TaskManagement.Infrastructure)
-- in the NexusCore 0.1.0.1405072806-mahdi packages.
-- Idempotent: safe to run again. For a database already upgraded with 1 to 6 (run those first).
-- Not needed when Database:InitializeOrgStructureSchema is true (the default): the API does the same
-- (the task_management columns are added by the API on every start-up anyway).
--   sqlcmd -S <server> -d TMPB -I -i db/upgrade-7-substitution.sql
SET QUOTED_IDENTIFIER ON;
GO

-- task_management: stand-ins and tasks owned through a post
IF OBJECT_ID(N'[task_management].[Tasks]') IS NOT NULL AND COL_LENGTH(N'task_management.Tasks', N'OwnerHeldByPost') IS NULL
    ALTER TABLE [task_management].[Tasks] ADD [OwnerHeldByPost] bit NOT NULL CONSTRAINT [DF_Tasks_OwnerHeldByPost] DEFAULT CAST(0 AS bit);
IF OBJECT_ID(N'[task_management].[TaskAssignees]') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'task_management.TaskAssignees', N'IsActing') IS NULL
        ALTER TABLE [task_management].[TaskAssignees] ADD [IsActing] bit NOT NULL CONSTRAINT [DF_TaskAssignees_IsActing] DEFAULT CAST(0 AS bit);
    IF COL_LENGTH(N'task_management.TaskAssignees', N'ActingNote') IS NULL
        ALTER TABLE [task_management].[TaskAssignees] ADD [ActingNote] nvarchar(200) NULL;
END
IF OBJECT_ID(N'[task_management].[TaskComments]') IS NOT NULL AND COL_LENGTH(N'task_management.TaskComments', N'ActingNote') IS NULL
    ALTER TABLE [task_management].[TaskComments] ADD [ActingNote] nvarchar(200) NULL;

GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020185002_AddPostActings'
)
BEGIN
    CREATE TABLE [org_structure].[PostActings] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [VacantPostId] uniqueidentifier NOT NULL,
        [ActingPostId] uniqueidentifier NOT NULL,
        [AppliedUserId] uniqueidentifier NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_PostActings] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020185002_AddPostActings'
)
BEGIN
    CREATE INDEX [IX_PostActings_ActingPostId] ON [org_structure].[PostActings] ([ActingPostId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020185002_AddPostActings'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PostActings_TenantId_VacantPostId] ON [org_structure].[PostActings] ([TenantId], [VacantPostId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020185002_AddPostActings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261020185002_AddPostActings', N'8.0.24');
END;
GO

COMMIT;
GO

