/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) so a project's
    charter start and end keep the time of day the user picked, not only the date.

    Adds two nullable columns; nothing is changed or dropped. Existing rows keep NULL, which
    means "date only" - exactly what they held before.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never
    adds a column to a table that already exists. Apply it after
    2026-09-22-add-task-and-subtask-times.sql. Safe to run more than once.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-09-23-add-task-charter-times.sql

    Equivalent EF Core migration, for databases managed with `dotnet ef database update`
    (do not run both):
      TaskManagementDbContext   20260923114910_AddTaskCharterTimes
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF COL_LENGTH(N'task_management.Tasks', N'CharterStartTime') IS NULL
    ALTER TABLE [task_management].[Tasks] ADD [CharterStartTime] time NULL;

IF COL_LENGTH(N'task_management.Tasks', N'CharterEndTime') IS NULL
    ALTER TABLE [task_management].[Tasks] ADD [CharterEndTime] time NULL;

-- Keep EF's migration history in step, when the database has one. Dynamic SQL, because a
-- database built by EnsureCreated has no history table and the batch must still compile.
IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]') IS NOT NULL
    EXEC (N'IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N''20260923114910_AddTaskCharterTimes'')
              INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
              VALUES (N''20260923114910_AddTaskCharterTimes'', N''8.0.24'');');

COMMIT TRANSACTION;
GO
