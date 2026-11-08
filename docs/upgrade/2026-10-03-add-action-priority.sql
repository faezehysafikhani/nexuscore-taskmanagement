/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) so each action
    carries a priority: Low = 0, Normal = 1, High = 2, Urgent = 3.

    Adds one NOT NULL column with a default of 1 (Normal), so every existing action keeps
    behaving as it did - they were all implicitly "normal". Nothing is changed or dropped.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never
    adds a column to a table that already exists. A host that prefers code can call
    Nexus.Actions.Infrastructure.ActionsSchemaUpgrade.EnsureCurrentAsync on startup instead; it
    runs the same statement. Safe to run more than once.

    Apply this BEFORE deploying the build that contains the Priority property: that build selects
    the column, so an Actions table without it fails every query.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-03-add-action-priority.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Skipped, without an error, on a database that does not have this module's tables yet: the module creates
-- its whole schema (this change included) the first time the host starts against such a database, and a
-- partial schema made here would stop it from doing so.
IF OBJECT_ID(N'[actions].[Actions]', N'U') IS NULL
BEGIN
    PRINT N'Skipped: Actions is not installed in this database ([actions].[Actions] does not exist). Nothing to upgrade.';
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;

IF COL_LENGTH(N'actions.Actions', N'Priority') IS NULL
    ALTER TABLE [actions].[Actions] ADD [Priority] int NOT NULL CONSTRAINT [DF_Actions_Priority] DEFAULT 1;

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
