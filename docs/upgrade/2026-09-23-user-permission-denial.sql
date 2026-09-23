/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) so an administrator
    can deny a permission to one user even when a role or group grants it.

    Adds one column, identity.UserPermissions.IsDenied (bit, NOT NULL, default 0). Existing rows
    get 0 - a direct grant, exactly what they meant before. Nothing is changed or dropped.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never
    adds a column to a table that already exists. Apply after 2026-09-22-user-administration.sql,
    with a backup first. Safe to run more than once.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-09-23-user-permission-denial.sql

    Equivalent EF Core migration, for databases managed with `dotnet ef database update`
    (do not run both):
      NexusCoreDbContext        20260923140445_AddUserPermissionDenial
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF COL_LENGTH(N'identity.UserPermissions', N'IsDenied') IS NULL
    ALTER TABLE [identity].[UserPermissions] ADD [IsDenied] bit NOT NULL DEFAULT CAST(0 AS bit);

-- Keep EF's migration history in step, when the database has one. Dynamic SQL, because a
-- database built by EnsureCreated has no history table and the batch must still compile.
IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]') IS NOT NULL
    EXEC (N'IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N''20260923140445_AddUserPermissionDenial'')
              INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
              VALUES (N''20260923140445_AddUserPermissionDenial'', N''8.0.24'');');

COMMIT TRANSACTION;
GO
