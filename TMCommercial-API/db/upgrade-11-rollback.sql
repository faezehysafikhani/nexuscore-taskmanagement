-- Rolls back db/upgrade-11-node-api.sql: drops the units' order and the node types' colour and icon. Those values
-- are LOST; everything else stays as it was. Use it only with the API/NexusCore packages from before this change
-- (a newer API would add them again). Does nothing when upgrade-11 is not applied. Back up the database first.
--   sqlcmd -S <server> -d TMPB -I -f 65001 -i db/upgrade-11-rollback.sql
SET QUOTED_IDENTIFIER ON;
GO

-- Drops a column and the default constraint SQL Server gave it (its name is generated).
CREATE OR ALTER PROCEDURE #DropColumnWithDefault @Table nvarchar(300), @Column sysname
AS
BEGIN
    DECLARE @constraint sysname = (
        SELECT dc.[name] FROM sys.default_constraints dc
        JOIN sys.columns c ON c.[object_id] = dc.[parent_object_id] AND c.[column_id] = dc.[parent_column_id]
        WHERE dc.[parent_object_id] = OBJECT_ID(@Table) AND c.[name] = @Column);
    IF @constraint IS NOT NULL
        EXEC(N'ALTER TABLE ' + @Table + N' DROP CONSTRAINT [' + @constraint + N']');
    IF COL_LENGTH(@Table, @Column) IS NOT NULL
        EXEC(N'ALTER TABLE ' + @Table + N' DROP COLUMN [' + @Column + N']');
END
GO

BEGIN TRANSACTION;
GO

IF EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261029192832_AddNodeTypeStyle')
BEGIN
    EXEC #DropColumnWithDefault N'[org_structure].[NodeTypes]', N'Color';
    EXEC #DropColumnWithDefault N'[org_structure].[NodeTypes]', N'Icon';
    DELETE FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261029192832_AddNodeTypeStyle';
END
ELSE
    PRINT N'org_structure: upgrade-11 is not applied, nothing to roll back.';
GO

IF EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261029192819_AddUnitSortOrder')
BEGIN
    EXEC #DropColumnWithDefault N'[organization].[OrganizationUnits]', N'SortOrder';
    DELETE FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261029192819_AddUnitSortOrder';
END
ELSE
    PRINT N'organization: upgrade-11 is not applied, nothing to roll back.';
GO

COMMIT;
GO

DROP PROCEDURE #DropColumnWithDefault;
GO
