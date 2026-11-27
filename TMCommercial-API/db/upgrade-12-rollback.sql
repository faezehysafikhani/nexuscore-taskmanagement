-- Rolls back db/upgrade-12-templates.sql: drops org_structure.StructureTemplates.Content. Templates saved from a
-- company are LOST (their rows stay, without content, and are not listed); everything else stays as it was. Use it
-- only with the API/NexusCore packages from before this change (a newer API would add the column again).
-- Does nothing when upgrade-12 is not applied. Back up the database first.
--   sqlcmd -S <server> -d TMPB -I -f 65001 -i db/upgrade-12-rollback.sql
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

IF EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261029214155_AddStructureTemplateContent')
BEGIN
    IF COL_LENGTH(N'org_structure.StructureTemplates', N'Content') IS NOT NULL
        ALTER TABLE [org_structure].[StructureTemplates] DROP COLUMN [Content];
    DELETE FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261029214155_AddStructureTemplateContent';
END
ELSE
    PRINT N'org_structure: upgrade-12 is not applied, nothing to roll back.';
GO

COMMIT;
GO
