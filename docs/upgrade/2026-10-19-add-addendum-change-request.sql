/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) so that a contract addendum can name the change request
    it carries out:
      - ContractAddenda.ChangeRequestId: the approved change request of the same project behind the addendum. An addendum without
        one is reported on the change dashboard as a change made without approval (a warning; nothing is refused).

    What it does: adds one NULLABLE column with an index. Every existing addendum has NULL - it names no change request, which is
    exactly what it was. Nothing is changed, dropped or updated, and running it again changes nothing.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never adds a column to a table that
    already exists. A host that prefers code can call
    Nexus.ProjectManagement.Contracts.Infrastructure.ContractsAddendumChangeSchemaUpgrade.EnsureCurrentAsync on startup instead; it runs
    the same statements in the same order.

    Apply this BEFORE deploying the build that contains it: that build selects the column, so a database without it fails every
    contract query.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-19-add-addendum-change-request.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Skipped, without an error, on a database that does not have this module's tables yet: the module creates
-- its whole schema (this change included) the first time the host starts against such a database, and a
-- partial schema made here would stop it from doing so.
IF OBJECT_ID(N'[project_contracts].[Contracts]', N'U') IS NULL
BEGIN
    PRINT N'Skipped: Contracts is not installed in this database ([project_contracts].[Contracts] does not exist). Nothing to upgrade.';
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;
GO

IF COL_LENGTH(N'project_contracts.ContractAddenda', N'ChangeRequestId') IS NULL
    ALTER TABLE [project_contracts].[ContractAddenda] ADD [ChangeRequestId] uniqueidentifier NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractAddenda_ChangeRequestId' AND object_id = OBJECT_ID(N'[project_contracts].[ContractAddenda]'))
    CREATE INDEX [IX_ContractAddenda_ChangeRequestId] ON [project_contracts].[ContractAddenda] ([ChangeRequestId]);
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
