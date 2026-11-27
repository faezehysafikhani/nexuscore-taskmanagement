-- OrgStructure (companies, positions, assignments, titles) schema for TMPB-API, generated from the EF Core migrations in the
-- NexusCore Nexus.OrgStructure.Infrastructure package (0.1.0.1405072501).
-- Idempotent: safe to run again; only migrations not yet applied run. For a database that already has these tables (created by an earlier version). Run 1 before 2.
--   sqlcmd -S <server> -d TMPB -I -i db/upgrade-2-org-structure.sql
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013111847_Baseline_OrgStructure'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013111847_Baseline_OrgStructure', N'8.0.24');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013112238_AddPositionParent'
)
BEGIN
    ALTER TABLE [org_structure].[Positions] ADD [ParentPositionId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013112238_AddPositionParent'
)
BEGIN
    CREATE INDEX [IX_Positions_ParentPositionId] ON [org_structure].[Positions] ([ParentPositionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013112238_AddPositionParent'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013112238_AddPositionParent', N'8.0.24');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013122348_RemovePositionCode'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[org_structure].[Positions]') AND [c].[name] = N'Code');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [org_structure].[Positions] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [org_structure].[Positions] DROP COLUMN [Code];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013122348_RemovePositionCode'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013122348_RemovePositionCode', N'8.0.24');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014111846_AddTeamsAndTitles'
)
BEGIN
    ALTER TABLE [org_structure].[UserAssignments] ADD [TeamId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014111846_AddTeamsAndTitles'
)
BEGIN
    CREATE TABLE [org_structure].[Honorifics] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Title] nvarchar(40) NOT NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_Honorifics] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014111846_AddTeamsAndTitles'
)
BEGIN
    CREATE TABLE [org_structure].[Teams] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UnitId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [LeaderUserId] uniqueidentifier NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_Teams] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014111846_AddTeamsAndTitles'
)
BEGIN
    CREATE TABLE [org_structure].[UserHonorifics] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [HonorificId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UserHonorifics] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014111846_AddTeamsAndTitles'
)
BEGIN
    CREATE INDEX [IX_UserAssignments_TeamId] ON [org_structure].[UserAssignments] ([TeamId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014111846_AddTeamsAndTitles'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Honorifics_TenantId_Title] ON [org_structure].[Honorifics] ([TenantId], [Title]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014111846_AddTeamsAndTitles'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Teams_TenantId_UnitId_Name] ON [org_structure].[Teams] ([TenantId], [UnitId], [Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014111846_AddTeamsAndTitles'
)
BEGIN
    CREATE INDEX [IX_UserHonorifics_HonorificId] ON [org_structure].[UserHonorifics] ([HonorificId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014111846_AddTeamsAndTitles'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UserHonorifics_TenantId_UserId] ON [org_structure].[UserHonorifics] ([TenantId], [UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014111846_AddTeamsAndTitles'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261014111846_AddTeamsAndTitles', N'8.0.24');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014130145_RemoveOrgTeams'
)
BEGIN
    DROP TABLE [org_structure].[Teams];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014130145_RemoveOrgTeams'
)
BEGIN
    DROP INDEX [IX_UserAssignments_TeamId] ON [org_structure].[UserAssignments];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014130145_RemoveOrgTeams'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[org_structure].[UserAssignments]') AND [c].[name] = N'TeamId');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [org_structure].[UserAssignments] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [org_structure].[UserAssignments] DROP COLUMN [TeamId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014130145_RemoveOrgTeams'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261014130145_RemoveOrgTeams', N'8.0.24');
END;
GO

COMMIT;
GO

