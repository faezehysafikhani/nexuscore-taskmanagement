-- Rolls back db/upgrade-10-org-nodes.sql: drops the node tables and the columns it added. Whatever they hold
-- (parents, node types, links, templates, marks) is LOST; the units, companies, posts and people stay as they were.
-- Use it only with the API/NexusCore packages from before this change (a newer API would add them again).
-- Does nothing when upgrade-10 is not applied. Back up the database first.
--   sqlcmd -S <server> -d TMPB -I -f 65001 -i db/upgrade-10-rollback.sql
SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261029181607_AddOrgNodes')
   AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261029181551_AddUnitNodeFields')
BEGIN
    PRINT N'upgrade-10 is not applied: nothing to roll back.';
    SET NOEXEC ON;
END
GO

-- Part 1/2: org_structure
BEGIN TRANSACTION;
GO

DROP TABLE [org_structure].[AutoLinkRules];
GO

DROP TABLE [org_structure].[LinkTypes];
GO

DROP TABLE [org_structure].[NodeTypePostTemplates];
GO

DROP TABLE [org_structure].[NodeTypeRules];
GO

DROP TABLE [org_structure].[NodeTypes];
GO

DROP TABLE [org_structure].[OrgChangeLogs];
GO

DROP TABLE [org_structure].[OrgLinks];
GO

DROP TABLE [org_structure].[StructureTemplateItems];
GO

DROP TABLE [org_structure].[StructureTemplates];
GO

DROP INDEX [IX_Companies_ParentCompanyId] ON [org_structure].[Companies];
GO

DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[org_structure].[UnitPositions]') AND [c].[name] = N'IsCompanyHead');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [org_structure].[UnitPositions] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [org_structure].[UnitPositions] DROP COLUMN [IsCompanyHead];
GO

DECLARE @var1 sysname;
SELECT @var1 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[org_structure].[UnitPositions]') AND [c].[name] = N'IsManagerPost');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [org_structure].[UnitPositions] DROP CONSTRAINT [' + @var1 + '];');
ALTER TABLE [org_structure].[UnitPositions] DROP COLUMN [IsManagerPost];
GO

DECLARE @var2 sysname;
SELECT @var2 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[org_structure].[UnitPositions]') AND [c].[name] = N'RowVersion');
IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [org_structure].[UnitPositions] DROP CONSTRAINT [' + @var2 + '];');
ALTER TABLE [org_structure].[UnitPositions] DROP COLUMN [RowVersion];
GO

DECLARE @var3 sysname;
SELECT @var3 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[org_structure].[Companies]') AND [c].[name] = N'ParentCompanyId');
IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [org_structure].[Companies] DROP CONSTRAINT [' + @var3 + '];');
ALTER TABLE [org_structure].[Companies] DROP COLUMN [ParentCompanyId];
GO

DECLARE @var4 sysname;
SELECT @var4 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[org_structure].[Companies]') AND [c].[name] = N'TemplateId');
IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [org_structure].[Companies] DROP CONSTRAINT [' + @var4 + '];');
ALTER TABLE [org_structure].[Companies] DROP COLUMN [TemplateId];
GO

DECLARE @var5 sysname;
SELECT @var5 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[org_structure].[Companies]') AND [c].[name] = N'UiLevel');
IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [org_structure].[Companies] DROP CONSTRAINT [' + @var5 + '];');
ALTER TABLE [org_structure].[Companies] DROP COLUMN [UiLevel];
GO

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261029181607_AddOrgNodes';
GO

COMMIT;
GO


-- Part 2/2: organization.OrganizationUnits
BEGIN TRANSACTION;
GO

DROP INDEX [IX_OrganizationUnits_NodeTypeId] ON [organization].[OrganizationUnits];
GO

DROP INDEX [IX_OrganizationUnits_ParentUnitId] ON [organization].[OrganizationUnits];
GO

DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[organization].[OrganizationUnits]') AND [c].[name] = N'NodeTypeId');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [organization].[OrganizationUnits] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [organization].[OrganizationUnits] DROP COLUMN [NodeTypeId];
GO

DECLARE @var1 sysname;
SELECT @var1 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[organization].[OrganizationUnits]') AND [c].[name] = N'ParentUnitId');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [organization].[OrganizationUnits] DROP CONSTRAINT [' + @var1 + '];');
ALTER TABLE [organization].[OrganizationUnits] DROP COLUMN [ParentUnitId];
GO

DECLARE @var2 sysname;
SELECT @var2 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[organization].[OrganizationUnits]') AND [c].[name] = N'RowVersion');
IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [organization].[OrganizationUnits] DROP CONSTRAINT [' + @var2 + '];');
ALTER TABLE [organization].[OrganizationUnits] DROP COLUMN [RowVersion];
GO

DECLARE @var3 sysname;
SELECT @var3 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[organization].[OrganizationUnits]') AND [c].[name] = N'ValidFrom');
IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [organization].[OrganizationUnits] DROP CONSTRAINT [' + @var3 + '];');
ALTER TABLE [organization].[OrganizationUnits] DROP COLUMN [ValidFrom];
GO

DECLARE @var4 sysname;
SELECT @var4 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[organization].[OrganizationUnits]') AND [c].[name] = N'ValidTo');
IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [organization].[OrganizationUnits] DROP CONSTRAINT [' + @var4 + '];');
ALTER TABLE [organization].[OrganizationUnits] DROP COLUMN [ValidTo];
GO

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261029181551_AddUnitNodeFields';
GO

COMMIT;
GO


SET NOEXEC OFF;
GO
