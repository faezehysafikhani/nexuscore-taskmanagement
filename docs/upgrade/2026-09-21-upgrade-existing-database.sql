/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) to the schema of
    this release: user profile fields, personal work teams, comment attachments and generated
    recurrence occurrences.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which only
    ever creates missing tables - it never adds a column to a table that already exists. A
    database created by an earlier version therefore needs these changes applied once.

    Safe to run more than once: every step checks first and skips what is already there.
    Nothing is dropped except one index that is replaced by a wider one, and one CHECK
    constraint that is replaced by a version that also allows comment attachments.

    Run it against the DefaultConnection database, with a backup taken first:

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-09-21-upgrade-existing-database.sql

    (-b stops at the first error; the transaction is then rolled back.)

    Equivalent EF Core migrations, for deployments that use `dotnet ef database update`:
      NexusCoreDbContext        20260921105343_SyncModelWithRuntimeSchema, 20260921105441_AddUserProfileAndPersonalTeams
      TaskManagementDbContext   20260921105514_AddCommentFilesAndGeneratedOccurrences
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;   -- required for the filtered indexes below
GO

BEGIN TRANSACTION;
GO

-------------------------------------------------------------------------------------------
-- identity.Users: profile, contact and preference fields
-------------------------------------------------------------------------------------------
IF COL_LENGTH(N'identity.Users', N'Username') IS NULL
    ALTER TABLE [identity].[Users] ADD [Username] nvarchar(64) NULL;
IF COL_LENGTH(N'identity.Users', N'PhoneNumber') IS NULL
    ALTER TABLE [identity].[Users] ADD [PhoneNumber] nvarchar(32) NULL;
IF COL_LENGTH(N'identity.Users', N'TelegramChatId') IS NULL
    ALTER TABLE [identity].[Users] ADD [TelegramChatId] nvarchar(64) NULL;
IF COL_LENGTH(N'identity.Users', N'NotifySms') IS NULL
    ALTER TABLE [identity].[Users] ADD [NotifySms] bit NOT NULL DEFAULT CAST(1 AS bit);
IF COL_LENGTH(N'identity.Users', N'NotifyTelegram') IS NULL
    ALTER TABLE [identity].[Users] ADD [NotifyTelegram] bit NOT NULL DEFAULT CAST(1 AS bit);
IF COL_LENGTH(N'identity.Users', N'AvatarUrl') IS NULL
    ALTER TABLE [identity].[Users] ADD [AvatarUrl] nvarchar(max) NULL;
IF COL_LENGTH(N'identity.Users', N'Theme') IS NULL
    ALTER TABLE [identity].[Users] ADD [Theme] nvarchar(40) NULL;
IF COL_LENGTH(N'identity.Users', N'ColorPalette') IS NULL
    ALTER TABLE [identity].[Users] ADD [ColorPalette] nvarchar(40) NULL;
IF COL_LENGTH(N'identity.Users', N'ThemeMode') IS NULL
    ALTER TABLE [identity].[Users] ADD [ThemeMode] nvarchar(10) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Users_TenantId_Username' AND object_id = OBJECT_ID(N'identity.Users'))
    CREATE UNIQUE INDEX [IX_Users_TenantId_Username] ON [identity].[Users] ([TenantId], [Username]) WHERE [Username] IS NOT NULL;
GO

-------------------------------------------------------------------------------------------
-- identity.UserGroups: personal work teams (OwnerUserId) and the wider unique name index
-------------------------------------------------------------------------------------------
IF COL_LENGTH(N'identity.UserGroups', N'OwnerUserId') IS NULL
    ALTER TABLE [identity].[UserGroups] ADD [OwnerUserId] uniqueidentifier NULL;
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_UserGroups_TenantId_NormalizedName' AND object_id = OBJECT_ID(N'identity.UserGroups'))
    DROP INDEX [IX_UserGroups_TenantId_NormalizedName] ON [identity].[UserGroups];

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_UserGroups_OwnerUserId' AND object_id = OBJECT_ID(N'identity.UserGroups'))
    CREATE INDEX [IX_UserGroups_OwnerUserId] ON [identity].[UserGroups] ([OwnerUserId]);

-- No filter on purpose: SQL Server treats NULLs as equal here, so organisational groups
-- (OwnerUserId NULL) keep unique names per tenant and personal teams are unique per owner.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_UserGroups_TenantId_OwnerUserId_NormalizedName' AND object_id = OBJECT_ID(N'identity.UserGroups'))
    CREATE UNIQUE INDEX [IX_UserGroups_TenantId_OwnerUserId_NormalizedName] ON [identity].[UserGroups] ([TenantId], [OwnerUserId], [NormalizedName]);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_UserGroups_Users_OwnerUserId')
    ALTER TABLE [identity].[UserGroups] ADD CONSTRAINT [FK_UserGroups_Users_OwnerUserId]
        FOREIGN KEY ([OwnerUserId]) REFERENCES [identity].[Users] ([Id]);
GO

-------------------------------------------------------------------------------------------
-- task_management: comment attachments and generated recurrence occurrences
-- (skipped entirely when the TaskManagement module was never installed in this database)
-------------------------------------------------------------------------------------------
IF OBJECT_ID(N'task_management.TaskFiles') IS NOT NULL AND COL_LENGTH(N'task_management.TaskFiles', N'CommentId') IS NULL
    ALTER TABLE [task_management].[TaskFiles] ADD [CommentId] uniqueidentifier NULL;
IF OBJECT_ID(N'task_management.SubTasks') IS NOT NULL AND COL_LENGTH(N'task_management.SubTasks', N'IsGeneratedOccurrence') IS NULL
    ALTER TABLE [task_management].[SubTasks] ADD [IsGeneratedOccurrence] bit NOT NULL DEFAULT CAST(0 AS bit);
GO

IF OBJECT_ID(N'task_management.TaskFiles') IS NOT NULL
BEGIN
    -- Replace the two-owner rule with the three-owner rule (task, subtask or comment).
    IF EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_TaskFiles_ExactlyOneOwner'
                 AND parent_object_id = OBJECT_ID(N'task_management.TaskFiles')
                 AND definition NOT LIKE N'%CommentId%')
        ALTER TABLE [task_management].[TaskFiles] DROP CONSTRAINT [CK_TaskFiles_ExactlyOneOwner];

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_TaskFiles_ExactlyOneOwner' AND parent_object_id = OBJECT_ID(N'task_management.TaskFiles'))
        ALTER TABLE [task_management].[TaskFiles] ADD CONSTRAINT [CK_TaskFiles_ExactlyOneOwner] CHECK (
            (CASE WHEN [TaskId] IS NOT NULL THEN 1 ELSE 0 END
           + CASE WHEN [SubTaskId] IS NOT NULL THEN 1 ELSE 0 END
           + CASE WHEN [CommentId] IS NOT NULL THEN 1 ELSE 0 END) = 1);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_TaskFiles_CommentId' AND object_id = OBJECT_ID(N'task_management.TaskFiles'))
        CREATE INDEX [IX_TaskFiles_CommentId] ON [task_management].[TaskFiles] ([CommentId]);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_TaskFiles_FileId_CommentId' AND object_id = OBJECT_ID(N'task_management.TaskFiles'))
        CREATE UNIQUE INDEX [IX_TaskFiles_FileId_CommentId] ON [task_management].[TaskFiles] ([FileId], [CommentId]) WHERE [CommentId] IS NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_TaskFiles_TaskComments_CommentId')
        ALTER TABLE [task_management].[TaskFiles] ADD CONSTRAINT [FK_TaskFiles_TaskComments_CommentId]
            FOREIGN KEY ([CommentId]) REFERENCES [task_management].[TaskComments] ([Id]);
END
GO

COMMIT TRANSACTION;
GO

PRINT N'Upgrade complete.';
