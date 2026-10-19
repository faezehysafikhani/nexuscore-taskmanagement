/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) for the links of a contract:
      - ContractLinks: a reference from a contract, or from one of its milestones or obligations, to a project document or an
        action (the contract's papers, and the evidence that a milestone was reached or an obligation met). The target is kept
        as its id and the title it had when it was linked; Contracts does not reference the Documents or Actions modules.

    What it does: creates [project_contracts].[ContractLinks] with an index on ContractId and one on SubjectId. Nothing is
    changed, dropped or updated, and running it again changes nothing.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never adds a table next to tables
    that already exist. A host that prefers code can call
    Nexus.ProjectManagement.Contracts.Infrastructure.ContractsLinksSchemaUpgrade.EnsureCurrentAsync on startup instead; it runs the same
    statements in the same order.

    Apply this BEFORE deploying the build that contains it.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-19-add-contract-links.sql
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

IF OBJECT_ID(N'[project_contracts].[ContractLinks]', N'U') IS NULL
CREATE TABLE [project_contracts].[ContractLinks] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [ContractId] uniqueidentifier NOT NULL,
    [SubjectType] int NOT NULL,
    [SubjectId] uniqueidentifier NOT NULL,
    [Kind] int NOT NULL,
    [TargetId] uniqueidentifier NOT NULL,
    [Title] nvarchar(300) NOT NULL,
    [Note] nvarchar(1000) NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [CreatedByUserId] uniqueidentifier NULL,
    [ModifiedAtUtc] datetimeoffset NULL,
    [ModifiedByUserId] uniqueidentifier NULL,
    CONSTRAINT [PK_ContractLinks] PRIMARY KEY ([Id])
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractLinks_ContractId' AND object_id = OBJECT_ID(N'[project_contracts].[ContractLinks]'))
    CREATE INDEX [IX_ContractLinks_ContractId] ON [project_contracts].[ContractLinks] ([ContractId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractLinks_SubjectId' AND object_id = OBJECT_ID(N'[project_contracts].[ContractLinks]'))
    CREATE INDEX [IX_ContractLinks_SubjectId] ON [project_contracts].[ContractLinks] ([SubjectId]);
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
