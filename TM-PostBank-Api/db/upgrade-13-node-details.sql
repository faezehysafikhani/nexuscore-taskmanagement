-- Node code and details (dynamic org structure, phase 6.2 and 6.5): five optional columns on
-- organization.OrganizationUnits: NodeCode (unique in its company, checked by the API; named NodeCode because
-- upgrade-1 removed the old Code column and check-upgrades.sql recognises step 1 by that), LatinName,
-- CorrespondenceTitle, Phone and Address. Generated from the EF Core migration AddNodeCodeAndDetails
-- (Nexus.Organization.Infrastructure). All nullable and empty for every existing unit; no row is added, changed or removed.
-- Phase 6's other parts (manager posts, import, parent company, dates, merge, undo) need no other change.
-- Idempotent: safe to run again. For a database already upgraded with 1 to 12 (run those first).
-- Not needed when Database:InitializeOrgStructureSchema is true (the default): the API does the same.
--   sqlcmd -S <server> -d TMPB -I -f 65001 -i db/upgrade-13-node-details.sql
-- Back up the database first. To undo: db/upgrade-13-rollback.sql.
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029223809_AddNodeCodeAndDetails'
)
BEGIN
    ALTER TABLE [organization].[OrganizationUnits] ADD [Address] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029223809_AddNodeCodeAndDetails'
)
BEGIN
    ALTER TABLE [organization].[OrganizationUnits] ADD [CorrespondenceTitle] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029223809_AddNodeCodeAndDetails'
)
BEGIN
    ALTER TABLE [organization].[OrganizationUnits] ADD [LatinName] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029223809_AddNodeCodeAndDetails'
)
BEGIN
    ALTER TABLE [organization].[OrganizationUnits] ADD [NodeCode] nvarchar(40) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029223809_AddNodeCodeAndDetails'
)
BEGIN
    ALTER TABLE [organization].[OrganizationUnits] ADD [Phone] nvarchar(40) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029223809_AddNodeCodeAndDetails'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261029223809_AddNodeCodeAndDetails', N'8.0.24');
END;
GO

COMMIT;
GO

