/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) for user
    administration: first and last name on users, the built-in administrator flagged as the
    system account, and permissions granted directly to users.

    Adds columns and one table; changes no existing value except the system flag of the seeded
    administrator (Id 33333333-3333-3333-3333-333333333333).

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never
    changes an existing table. Apply after 2026-09-22-signin-by-username-or-phone.sql, with a
    backup first. Safe to run more than once.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-09-22-user-administration.sql

    Equivalent EF Core migrations, for databases managed with `dotnet ef database update`
    (do not run both):
      NexusCoreDbContext        20260922114548_UserNamesSystemAccountAndDirectPermissions
      TaskManagementDbContext   20260922114632_SyncUserModelWithCore (no schema change)
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

IF COL_LENGTH(N'identity.Users', N'FirstName') IS NULL
    ALTER TABLE [identity].[Users] ADD [FirstName] nvarchar(80) NULL;
IF COL_LENGTH(N'identity.Users', N'LastName') IS NULL
    ALTER TABLE [identity].[Users] ADD [LastName] nvarchar(80) NULL;
IF COL_LENGTH(N'identity.Users', N'IsSystem') IS NULL
    ALTER TABLE [identity].[Users] ADD [IsSystem] bit NOT NULL CONSTRAINT [DF_Users_IsSystem] DEFAULT (CAST(0 AS bit));
GO

UPDATE [identity].[Users] SET [IsSystem] = 1
WHERE [Id] = '33333333-3333-3333-3333-333333333333' AND [IsSystem] = 0;
GO

IF OBJECT_ID(N'[identity].[UserPermissions]') IS NULL
BEGIN
    CREATE TABLE [identity].[UserPermissions] (
        [UserId] uniqueidentifier NOT NULL,
        [PermissionId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_UserPermissions] PRIMARY KEY ([UserId], [PermissionId]),
        CONSTRAINT [FK_UserPermissions_Permissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [identity].[Permissions] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_UserPermissions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [identity].[Users] ([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_UserPermissions_PermissionId] ON [identity].[UserPermissions] ([PermissionId]);
END
GO

-- Keep EF's migration history in step, when the database has one.
IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]') IS NOT NULL
    EXEC (N'
        IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N''20260922114548_UserNamesSystemAccountAndDirectPermissions'')
            INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N''20260922114548_UserNamesSystemAccountAndDirectPermissions'', N''8.0.24'');
        IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N''20260922114632_SyncUserModelWithCore'')
            INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N''20260922114632_SyncUserModelWithCore'', N''8.0.24'');');
GO

COMMIT TRANSACTION;
GO
