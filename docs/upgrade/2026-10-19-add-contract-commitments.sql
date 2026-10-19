/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) for the milestones and obligations of a contract:
      - ContractMilestones: dated points of a contract the other party has to reach (title, due date, status, the day it was completed);
      - ContractObligations: what a contract binds its parties to (title, who owes it, optional due date, status, the day it was met);
      - an optional ScopeOfWork text on every contract (what the contract covers).

    What it does: creates the two tables with an index on ContractId each and adds one NULLABLE column (Contracts.ScopeOfWork).
    Existing contracts simply have no milestones, no obligations and no scope. Nothing is changed or dropped, and nothing is
    updated, so running it again changes nothing.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never adds a table or a column next to
    tables that already exist. A host that prefers code can call
    Nexus.ProjectManagement.Contracts.Infrastructure.ContractsCommitmentsSchemaUpgrade.EnsureCurrentAsync on startup instead; it runs the same
    statements in the same order.

    Apply this BEFORE deploying the build that contains it: that build selects the new column, so a database without it fails every
    contract query.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-19-add-contract-commitments.sql
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

IF OBJECT_ID(N'[project_contracts].[ContractMilestones]', N'U') IS NULL
CREATE TABLE [project_contracts].[ContractMilestones] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [ContractId] uniqueidentifier NOT NULL,
    [Title] nvarchar(300) NOT NULL,
    [Description] nvarchar(2000) NULL,
    [DueDate] date NOT NULL,
    [Status] int NOT NULL,
    [CompletedDate] date NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [CreatedByUserId] uniqueidentifier NULL,
    [ModifiedAtUtc] datetimeoffset NULL,
    [ModifiedByUserId] uniqueidentifier NULL,
    CONSTRAINT [PK_ContractMilestones] PRIMARY KEY ([Id])
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractMilestones_ContractId' AND object_id = OBJECT_ID(N'[project_contracts].[ContractMilestones]'))
    CREATE INDEX [IX_ContractMilestones_ContractId] ON [project_contracts].[ContractMilestones] ([ContractId]);
GO

IF OBJECT_ID(N'[project_contracts].[ContractObligations]', N'U') IS NULL
CREATE TABLE [project_contracts].[ContractObligations] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [ContractId] uniqueidentifier NOT NULL,
    [Title] nvarchar(300) NOT NULL,
    [Description] nvarchar(2000) NULL,
    [Party] int NOT NULL,
    [DueDate] date NULL,
    [Status] int NOT NULL,
    [FulfilledDate] date NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [CreatedByUserId] uniqueidentifier NULL,
    [ModifiedAtUtc] datetimeoffset NULL,
    [ModifiedByUserId] uniqueidentifier NULL,
    CONSTRAINT [PK_ContractObligations] PRIMARY KEY ([Id])
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractObligations_ContractId' AND object_id = OBJECT_ID(N'[project_contracts].[ContractObligations]'))
    CREATE INDEX [IX_ContractObligations_ContractId] ON [project_contracts].[ContractObligations] ([ContractId]);
GO

IF COL_LENGTH(N'project_contracts.Contracts', N'ScopeOfWork') IS NULL
    ALTER TABLE [project_contracts].[Contracts] ADD [ScopeOfWork] nvarchar(4000) NULL;
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
