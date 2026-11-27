-- Rolls back db/upgrade-19-position-permissions.sql: the tables PositionPermissions and UnitPositionPermissions are
-- dropped, so every permission given by a position or a post is lost (users keep what their roles, groups and direct
-- grants give). Use it only with the API/NexusCore packages from before this change. Does nothing when upgrade-19 is
-- not applied. Back up the database first.
--   sqlcmd -S <server> -d TMPB -I -f 65001 -i db/upgrade-19-rollback.sql
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

IF EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261107071228_AddPositionPermissions')
BEGIN
    DROP TABLE [org_structure].[UnitPositionPermissions];
    DROP TABLE [org_structure].[PositionPermissions];
    DELETE FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261107071228_AddPositionPermissions';
END
ELSE
    PRINT N'org_structure: upgrade-19 is not applied, nothing to roll back.';
GO

COMMIT;
GO
