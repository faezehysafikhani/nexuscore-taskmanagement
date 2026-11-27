-- Company profile (org structure): org_structure.Companies gets eight optional columns - Phone, Email, Website,
-- NationalId, EconomicCode, Address, HeadTitle (what the chart calls the company's head; empty: «رئیس کل») and Logo
-- (where its logo file is kept). All nullable and empty for every existing company; no row is added, changed or
-- removed. Generated from the EF Core migration AddCompanyProfile (Nexus.OrgStructure.Infrastructure).
-- Idempotent: safe to run again. For a database already upgraded with 1 to 14 (run those first).
-- Not needed when Database:InitializeOrgStructureSchema is true (the default): the API does the same.
--   sqlcmd -S <server> -d TMPB -I -f 65001 -i db/upgrade-15-company-profile.sql
-- Back up the database first. To undo: db/upgrade-15-rollback.sql.
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031103205_AddCompanyProfile'
)
BEGIN
    ALTER TABLE [org_structure].[Companies] ADD [Address] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031103205_AddCompanyProfile'
)
BEGIN
    ALTER TABLE [org_structure].[Companies] ADD [EconomicCode] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031103205_AddCompanyProfile'
)
BEGIN
    ALTER TABLE [org_structure].[Companies] ADD [Email] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031103205_AddCompanyProfile'
)
BEGIN
    ALTER TABLE [org_structure].[Companies] ADD [HeadTitle] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031103205_AddCompanyProfile'
)
BEGIN
    ALTER TABLE [org_structure].[Companies] ADD [Logo] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031103205_AddCompanyProfile'
)
BEGIN
    ALTER TABLE [org_structure].[Companies] ADD [NationalId] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031103205_AddCompanyProfile'
)
BEGIN
    ALTER TABLE [org_structure].[Companies] ADD [Phone] nvarchar(40) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031103205_AddCompanyProfile'
)
BEGIN
    ALTER TABLE [org_structure].[Companies] ADD [Website] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031103205_AddCompanyProfile'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261031103205_AddCompanyProfile', N'8.0.24');
END;
GO

COMMIT;
GO

