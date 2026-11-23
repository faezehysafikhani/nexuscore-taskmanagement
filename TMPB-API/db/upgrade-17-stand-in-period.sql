-- Stand-in for a set time (org structure): org_structure.PostActings gets two optional columns - StartDate and
-- EndDate. With an end date a stand-in (جانشین) is on from its start until its end, also while the post has a holder
-- (leave, a mission); without them it lasts while the post is vacant, as before. Both empty for every existing
-- stand-in; no row is added, changed or removed. Generated from the EF Core migration AddPostActingPeriod
-- (Nexus.OrgStructure.Infrastructure). Idempotent: safe to run again. For a database already upgraded with 1 to 16.
-- Not needed when Database:InitializeOrgStructureSchema is true (the default): the API does the same.
--   sqlcmd -S <server> -d TMPB -I -f 65001 -i db/upgrade-17-stand-in-period.sql
-- Back up the database first. To undo: db/upgrade-17-rollback.sql.
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031163833_AddPostActingPeriod'
)
BEGIN
    ALTER TABLE [org_structure].[PostActings] ADD [EndDate] datetimeoffset NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031163833_AddPostActingPeriod'
)
BEGIN
    ALTER TABLE [org_structure].[PostActings] ADD [StartDate] datetimeoffset NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031163833_AddPostActingPeriod'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261031163833_AddPostActingPeriod', N'8.0.24');
END;
GO

COMMIT;
GO

