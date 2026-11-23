-- Rolls back db/upgrade-17-stand-in-period.sql: PostActings.StartDate and EndDate are dropped. Stand-ins for a set
-- time are removed first (without their dates they would last as long as the post is vacant). Use it only with the
-- API/NexusCore packages from before this change. Does nothing when upgrade-17 is not applied. Back up the database first.
--   sqlcmd -S <server> -d TMPB -I -f 65001 -i db/upgrade-17-rollback.sql
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

IF EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261031163833_AddPostActingPeriod')
BEGIN
    EXEC(N'DELETE FROM [org_structure].[PostActings] WHERE [EndDate] IS NOT NULL;');
    ALTER TABLE [org_structure].[PostActings] DROP COLUMN [EndDate];
    ALTER TABLE [org_structure].[PostActings] DROP COLUMN [StartDate];
    DELETE FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261031163833_AddPostActingPeriod';
END
ELSE
    PRINT N'org_structure: upgrade-17 is not applied, nothing to roll back.';
GO

COMMIT;
GO
