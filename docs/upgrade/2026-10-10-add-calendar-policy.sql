/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) so a work calendar knows how long
    a working day is and whether it follows the country's official holidays.

    Adds two NOT NULL columns to [calendar].[WorkCalendars]:
      WorkHoursPerDay        int  DEFAULT 8
      ApplyOfficialHolidays  bit  DEFAULT 0   (existing calendars do NOT start following official holidays,
                                               so nothing already scheduled moves; new calendars created
                                               through the API follow them unless told otherwise)

    Nothing is changed or dropped. Why a script: hosts create schemas with ModuleSchemaInitializer
    (EnsureCreated), which never adds a column to a table that already exists. A host that prefers code can call
    Nexus.Calendar.Infrastructure.CalendarSchemaUpgrade.EnsureCurrentAsync on startup instead; it runs the same
    statements. Safe to run more than once.

    Apply this BEFORE deploying the build that contains these settings: that build selects the columns, so a
    WorkCalendars table without them fails every query.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-10-add-calendar-policy.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Skipped, without an error, on a database that does not have this module's tables yet: the module creates
-- its whole schema (this change included) the first time the host starts against such a database, and a
-- partial schema made here would stop it from doing so.
IF OBJECT_ID(N'[calendar].[WorkCalendars]', N'U') IS NULL
BEGIN
    PRINT N'Skipped: Calendar is not installed in this database ([calendar].[WorkCalendars] does not exist). Nothing to upgrade.';
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;
GO

IF COL_LENGTH(N'calendar.WorkCalendars', N'WorkHoursPerDay') IS NULL
    ALTER TABLE [calendar].[WorkCalendars] ADD [WorkHoursPerDay] int NOT NULL CONSTRAINT [DF_WorkCalendars_WorkHoursPerDay] DEFAULT 8;
GO

IF COL_LENGTH(N'calendar.WorkCalendars', N'ApplyOfficialHolidays') IS NULL
    ALTER TABLE [calendar].[WorkCalendars] ADD [ApplyOfficialHolidays] bit NOT NULL CONSTRAINT [DF_WorkCalendars_ApplyOfficialHolidays] DEFAULT 0;
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
