/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) so the strategy tree remembers the order the
    strategies were arranged in: adds [strategy].[Strategies].[SortOrder] int NOT NULL DEFAULT 0.

    Every existing strategy gets 0, which orders them by name exactly as they were listed before. Nothing is changed or dropped.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never adds a column to a table that
    already exists. A host that prefers code can call Nexus.StrategyManagement.Infrastructure.StrategySchemaUpgrade.EnsureCurrentAsync
    on startup instead; it runs the same statement. Safe to run more than once.

    Apply this BEFORE deploying the build that contains the strategy tree: that build selects the column, so a Strategies table
    without it fails every query.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-14-add-strategy-sort-order.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Skipped, without an error, on a database that does not have this module's tables yet: the module creates
-- its whole schema (this change included) the first time the host starts against such a database, and a
-- partial schema made here would stop it from doing so.
IF OBJECT_ID(N'[strategy].[Strategies]', N'U') IS NULL
BEGIN
    PRINT N'Skipped: Strategy is not installed in this database ([strategy].[Strategies] does not exist). Nothing to upgrade.';
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;
GO

IF COL_LENGTH(N'strategy.Strategies', N'SortOrder') IS NULL
    ALTER TABLE [strategy].[Strategies] ADD [SortOrder] int NOT NULL CONSTRAINT [DF_Strategies_SortOrder] DEFAULT 0;
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
