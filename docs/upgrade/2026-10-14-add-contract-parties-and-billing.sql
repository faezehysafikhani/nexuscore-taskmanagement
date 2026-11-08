/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) for three small additions to project contracts:
      - a contract can name its supervisor and its beneficiary (besides the contractor, which is the existing counterparty);
      - an addendum can say which period it covers (start / end date);
      - an invoice can have a title and say which period it bills for (start / end date).

    What it does: adds seven NULLABLE columns (Contracts: SupervisorName, BeneficiaryName; ContractAddenda: StartDate, EndDate;
    ContractInvoices: Title, PeriodStartDate, PeriodEndDate). Every existing row has NULL in all of them, which means "not
    given" - exactly how they have always behaved. Nothing is changed or dropped.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never adds a column to a table that
    already exists. A host that prefers code can call
    Nexus.ProjectManagement.Contracts.Infrastructure.ContractsPartiesSchemaUpgrade.EnsureCurrentAsync on startup instead; it runs the same
    statements in the same order. Safe to run more than once.

    Apply this BEFORE deploying the build that contains these fields: that build selects the columns, so tables without them
    fail every query.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-14-add-contract-parties-and-billing.sql
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

IF COL_LENGTH(N'project_contracts.Contracts', N'SupervisorName') IS NULL
    ALTER TABLE [project_contracts].[Contracts] ADD [SupervisorName] nvarchar(300) NULL;
GO

IF COL_LENGTH(N'project_contracts.Contracts', N'BeneficiaryName') IS NULL
    ALTER TABLE [project_contracts].[Contracts] ADD [BeneficiaryName] nvarchar(300) NULL;
GO

IF COL_LENGTH(N'project_contracts.ContractAddenda', N'StartDate') IS NULL
    ALTER TABLE [project_contracts].[ContractAddenda] ADD [StartDate] date NULL;
GO

IF COL_LENGTH(N'project_contracts.ContractAddenda', N'EndDate') IS NULL
    ALTER TABLE [project_contracts].[ContractAddenda] ADD [EndDate] date NULL;
GO

IF COL_LENGTH(N'project_contracts.ContractInvoices', N'Title') IS NULL
    ALTER TABLE [project_contracts].[ContractInvoices] ADD [Title] nvarchar(300) NULL;
GO

IF COL_LENGTH(N'project_contracts.ContractInvoices', N'PeriodStartDate') IS NULL
    ALTER TABLE [project_contracts].[ContractInvoices] ADD [PeriodStartDate] date NULL;
GO

IF COL_LENGTH(N'project_contracts.ContractInvoices', N'PeriodEndDate') IS NULL
    ALTER TABLE [project_contracts].[ContractInvoices] ADD [PeriodEndDate] date NULL;
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
