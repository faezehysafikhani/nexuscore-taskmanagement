-- Appointments over time (dynamic org structure, phase 9): org_structure.UserPositions gets StartDate and EndDate
-- (nullable). Today's rows get StartDate = the day they were saved (CreatedAtUtc) and no EndDate, so each is held as
-- now. The unique index "one person per post" becomes "one open appointment (no EndDate) per post"; on today's data it
-- is the same rows. Generated from the EF Core migration AddAppointmentPeriods (Nexus.OrgStructure.Infrastructure).
-- No row is added or removed. From now on taking a post off someone ends their appointment (it stays as history).
-- Idempotent: safe to run again. For a database already upgraded with 1 to 13 (run those first).
-- Not needed when Database:InitializeOrgStructureSchema is true (the default): the API does the same.
--   sqlcmd -S <server> -d TMPB -I -f 65001 -i db/upgrade-14-appointments.sql
-- Back up the database first. To undo: db/upgrade-14-rollback.sql.
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029234908_AddAppointmentPeriods'
)
BEGIN
    DROP INDEX [IX_UserPositions_TenantId_UnitPositionId] ON [org_structure].[UserPositions];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029234908_AddAppointmentPeriods'
)
BEGIN
    ALTER TABLE [org_structure].[UserPositions] ADD [EndDate] datetimeoffset NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029234908_AddAppointmentPeriods'
)
BEGIN
    ALTER TABLE [org_structure].[UserPositions] ADD [StartDate] datetimeoffset NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029234908_AddAppointmentPeriods'
)
BEGIN
    UPDATE [org_structure].[UserPositions] SET [StartDate] = [CreatedAtUtc] WHERE [StartDate] IS NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029234908_AddAppointmentPeriods'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_UserPositions_TenantId_UnitPositionId] ON [org_structure].[UserPositions] ([TenantId], [UnitPositionId]) WHERE [EndDate] IS NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029234908_AddAppointmentPeriods'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261029234908_AddAppointmentPeriods', N'8.0.24');
END;
GO

COMMIT;
GO

