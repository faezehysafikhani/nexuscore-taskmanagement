-- Rolls back db/upgrade-13-node-details.sql: drops the units' NodeCode, LatinName, CorrespondenceTitle, Phone and
-- Address. Those values are LOST; everything else stays as it was. Use it only with the API/NexusCore packages from
-- before this change (a newer API would add them again). Does nothing when upgrade-13 is not applied.
-- Back up the database first.
--   sqlcmd -S <server> -d TMPB -I -f 65001 -i db/upgrade-13-rollback.sql
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

IF EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261029223809_AddNodeCodeAndDetails')
BEGIN
    IF COL_LENGTH(N'organization.OrganizationUnits', N'NodeCode') IS NOT NULL ALTER TABLE [organization].[OrganizationUnits] DROP COLUMN [NodeCode];
    IF COL_LENGTH(N'organization.OrganizationUnits', N'LatinName') IS NOT NULL ALTER TABLE [organization].[OrganizationUnits] DROP COLUMN [LatinName];
    IF COL_LENGTH(N'organization.OrganizationUnits', N'CorrespondenceTitle') IS NOT NULL ALTER TABLE [organization].[OrganizationUnits] DROP COLUMN [CorrespondenceTitle];
    IF COL_LENGTH(N'organization.OrganizationUnits', N'Phone') IS NOT NULL ALTER TABLE [organization].[OrganizationUnits] DROP COLUMN [Phone];
    IF COL_LENGTH(N'organization.OrganizationUnits', N'Address') IS NOT NULL ALTER TABLE [organization].[OrganizationUnits] DROP COLUMN [Address];
    DELETE FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261029223809_AddNodeCodeAndDetails';
END
ELSE
    PRINT N'organization: upgrade-13 is not applied, nothing to roll back.';
GO

COMMIT;
GO
