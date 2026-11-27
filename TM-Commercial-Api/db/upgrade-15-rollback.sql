-- Rolls back db/upgrade-15-company-profile.sql: the eight company profile columns are dropped. Their values (phone,
-- email, website, national id, economic code, address, head title and the logo's place) are LOST; the logo files stay
-- in the API's file storage, unused. Use it only with the API/NexusCore packages from before this change.
-- Does nothing when upgrade-15 is not applied. Back up the database first.
--   sqlcmd -S <server> -d TMPB -I -f 65001 -i db/upgrade-15-rollback.sql
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

IF EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261031103205_AddCompanyProfile')
BEGIN
    ALTER TABLE [org_structure].[Companies] DROP COLUMN [Address];
    ALTER TABLE [org_structure].[Companies] DROP COLUMN [EconomicCode];
    ALTER TABLE [org_structure].[Companies] DROP COLUMN [Email];
    ALTER TABLE [org_structure].[Companies] DROP COLUMN [HeadTitle];
    ALTER TABLE [org_structure].[Companies] DROP COLUMN [Logo];
    ALTER TABLE [org_structure].[Companies] DROP COLUMN [NationalId];
    ALTER TABLE [org_structure].[Companies] DROP COLUMN [Phone];
    ALTER TABLE [org_structure].[Companies] DROP COLUMN [Website];
    DELETE FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261031103205_AddCompanyProfile';
END
ELSE
    PRINT N'org_structure: upgrade-15 is not applied, nothing to roll back.';
GO

COMMIT;
GO
