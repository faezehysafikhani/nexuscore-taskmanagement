-- Structure templates (dynamic org structure, phase 4): a column for a saved template's whole content
-- (org_structure.StructureTemplates.Content, JSON). Generated from the EF Core migration AddStructureTemplateContent
-- (Nexus.OrgStructure.Infrastructure). The column is new and empty (nullable); no row is added, changed or removed.
-- Secondary links, automatic link rules and the built-in templates need no other change (their tables came with upgrade-10).
-- Idempotent: safe to run again. For a database already upgraded with 1 to 11 (run those first).
-- Not needed when Database:InitializeOrgStructureSchema is true (the default): the API does the same.
--   sqlcmd -S <server> -d TMPB -I -f 65001 -i db/upgrade-12-templates.sql
-- Back up the database first. To undo: db/upgrade-12-rollback.sql.
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029214155_AddStructureTemplateContent'
)
BEGIN
    ALTER TABLE [org_structure].[StructureTemplates] ADD [Content] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029214155_AddStructureTemplateContent'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261029214155_AddStructureTemplateContent', N'8.0.24');
END;
GO

COMMIT;
GO

