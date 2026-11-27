-- Node API (dynamic org structure, phase 2): sibling order for units (organization.OrganizationUnits.SortOrder)
-- and a colour and icon for node types (org_structure.NodeTypes.Color / Icon). Generated from the EF Core
-- migrations AddUnitSortOrder (Nexus.Organization.Infrastructure) and AddNodeTypeStyle (Nexus.OrgStructure.Infrastructure).
-- Today's data keeps its meaning: every unit gets order 0 (the chart keeps ordering them by name), every node
-- type the default colour #2563eb and icon «folder». Nothing else changes; no row is added or removed.
-- Idempotent: safe to run again. For a database already upgraded with 1 to 10 (run those first).
-- Not needed when Database:InitializeOrgStructureSchema is true (the default): the API does the same.
--   sqlcmd -S <server> -d TMPB -I -f 65001 -i db/upgrade-11-node-api.sql
-- Back up the database first. To undo: db/upgrade-11-rollback.sql.
SET QUOTED_IDENTIFIER ON;
GO

-- Part 1/2: organization.OrganizationUnits
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029192819_AddUnitSortOrder'
)
BEGIN
    ALTER TABLE [organization].[OrganizationUnits] ADD [SortOrder] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029192819_AddUnitSortOrder'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261029192819_AddUnitSortOrder', N'8.0.24');
END;
GO

COMMIT;
GO


-- Part 2/2: org_structure.NodeTypes
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029192832_AddNodeTypeStyle'
)
BEGIN
    ALTER TABLE [org_structure].[NodeTypes] ADD [Color] nvarchar(7) NOT NULL DEFAULT N'#2563eb';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029192832_AddNodeTypeStyle'
)
BEGIN
    ALTER TABLE [org_structure].[NodeTypes] ADD [Icon] nvarchar(40) NOT NULL DEFAULT N'folder';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029192832_AddNodeTypeStyle'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261029192832_AddNodeTypeStyle', N'8.0.24');
END;
GO

COMMIT;
GO

