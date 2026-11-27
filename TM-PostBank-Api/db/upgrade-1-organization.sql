-- Organization (deputies / units) schema for TMPB-API, generated from the EF Core migrations in the
-- NexusCore Nexus.Organization.Infrastructure package (0.1.0.1405072501).
-- Idempotent: safe to run again; only migrations not yet applied run. For a database that already has these tables (created by an earlier version). Run 1 before 2.
--   sqlcmd -S <server> -d TMPB -I -i db/upgrade-1-organization.sql
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
    WHERE [MigrationId] = N'20261013111237_RemoveUnitCodeParent'
)
BEGIN
    IF SCHEMA_ID(N'organization') IS NULL EXEC(N'CREATE SCHEMA [organization];');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013111237_RemoveUnitCodeParent'
)
BEGIN

    -- Drop indexes and foreign keys that reference Code (best-effort)
    IF COL_LENGTH(N'organization.OrganizationUnits', N'Code') IS NOT NULL
    BEGIN
        DECLARE @ixname nvarchar(128);
        SELECT TOP(1) @ixname = i.name FROM sys.indexes i
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(N'organization.OrganizationUnits') AND c.name = 'Code';
        IF @ixname IS NOT NULL
            EXEC('DROP INDEX [' + @ixname + '] ON [organization].[OrganizationUnits]');

        ALTER TABLE [organization].[OrganizationUnits] DROP COLUMN [Code];
    END

    -- Drop any foreign key constraints referencing ParentId, then drop indexes on ParentId, then drop the ParentId column
    WHILE EXISTS (SELECT 1 FROM sys.foreign_key_columns fkc JOIN sys.columns c ON fkc.parent_object_id = c.object_id AND fkc.parent_column_id = c.column_id WHERE c.object_id = OBJECT_ID(N'organization.OrganizationUnits') AND c.name = 'ParentId')
    BEGIN
        DECLARE @fkname nvarchar(128);
        SELECT TOP(1) @fkname = fk.name FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            JOIN sys.columns c ON fkc.parent_object_id = c.object_id AND fkc.parent_column_id = c.column_id
            WHERE c.object_id = OBJECT_ID(N'organization.OrganizationUnits') AND c.name = 'ParentId';
        IF @fkname IS NOT NULL
            EXEC('ALTER TABLE [organization].[OrganizationUnits] DROP CONSTRAINT [' + @fkname + ']');
    END

    WHILE EXISTS (SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE i.object_id = OBJECT_ID(N'organization.OrganizationUnits') AND c.name = 'ParentId')
    BEGIN
        DECLARE @ixname2 nvarchar(128);
        SELECT TOP(1) @ixname2 = i.name FROM sys.indexes i
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(N'organization.OrganizationUnits') AND c.name = 'ParentId';
        IF @ixname2 IS NOT NULL
            EXEC('DROP INDEX [' + @ixname2 + '] ON [organization].[OrganizationUnits]');
    END

    IF COL_LENGTH(N'organization.OrganizationUnits', N'ParentId') IS NOT NULL
    BEGIN
        ALTER TABLE [organization].[OrganizationUnits] DROP COLUMN [ParentId];
    END

END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013111237_RemoveUnitCodeParent'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013111237_RemoveUnitCodeParent', N'8.0.24');
END;
GO

COMMIT;
GO

