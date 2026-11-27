-- Rolls back db/upgrade-14-appointments.sql: back to one row per post. The HISTORY IS LOST: ended appointments and
-- ones that have not started yet are deleted (the one on now stays, even if its end is already set), then StartDate and EndDate are dropped and the old unique index comes
-- back. Who holds each post now stays. Use it only with the API/NexusCore packages from before this change.
-- Does nothing when upgrade-14 is not applied. Back up the database first.
--   sqlcmd -S <server> -d TMPB -I -f 65001 -i db/upgrade-14-rollback.sql
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

IF EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261029234908_AddAppointmentPeriods')
BEGIN
    DROP INDEX [IX_UserPositions_TenantId_UnitPositionId] ON [org_structure].[UserPositions];
    -- Only the appointment on now stays for each post (also one whose end is already set for a later day).
    EXEC(N'DELETE FROM [org_structure].[UserPositions] WHERE [StartDate] > SYSDATETIMEOFFSET() OR [EndDate] <= SYSDATETIMEOFFSET()');
    ALTER TABLE [org_structure].[UserPositions] DROP COLUMN [EndDate];
    ALTER TABLE [org_structure].[UserPositions] DROP COLUMN [StartDate];
    CREATE UNIQUE INDEX [IX_UserPositions_TenantId_UnitPositionId] ON [org_structure].[UserPositions] ([TenantId], [UnitPositionId]);
    DELETE FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261029234908_AddAppointmentPeriods';
END
ELSE
    PRINT N'org_structure: upgrade-14 is not applied, nothing to roll back.';
GO

COMMIT;
GO
