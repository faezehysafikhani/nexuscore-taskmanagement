/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) for risk-response reports: the person responsible
    for a risk's response reports where the work stands (planning / in progress / on hold / completed) and an approver
    verifies it; until then the risk keeps its response status.

    What it does, in order:
      1. creates [risk_management].[RiskResponseSubmissions];
      2. creates its two lookup indexes (by project, by risk).

    Nothing existing is changed or dropped.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never adds a table to a schema
    that already exists. A host that prefers code can call
    Nexus.ProjectManagement.RiskManagement.Infrastructure.RiskResponseSchemaUpgrade.EnsureCurrentAsync on startup instead; it runs
    the same statements in the same order. Safe to run more than once.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-14-add-risk-response-submissions.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Skipped, without an error, on a database that does not have this module's tables yet: the module creates
-- its whole schema (this change included) the first time the host starts against such a database, and a
-- partial schema made here would stop it from doing so.
IF OBJECT_ID(N'[risk_management].[Risks]', N'U') IS NULL
BEGIN
    PRINT N'Skipped: Risk management is not installed in this database ([risk_management].[Risks] does not exist). Nothing to upgrade.';
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;
GO

IF OBJECT_ID(N'[risk_management].[RiskResponseSubmissions]', N'U') IS NULL
    CREATE TABLE [risk_management].[RiskResponseSubmissions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [RiskId] uniqueidentifier NOT NULL,
        [PreviousStatus] int NOT NULL,
        [SubmittedStatus] int NOT NULL,
        [Comment] nvarchar(2000) NULL,
        [ApprovalStatus] int NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_RiskResponseSubmissions] PRIMARY KEY ([Id])
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RiskResponseSubmissions_ProjectId' AND object_id = OBJECT_ID(N'[risk_management].[RiskResponseSubmissions]'))
    CREATE INDEX [IX_RiskResponseSubmissions_ProjectId] ON [risk_management].[RiskResponseSubmissions] ([ProjectId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RiskResponseSubmissions_RiskId' AND object_id = OBJECT_ID(N'[risk_management].[RiskResponseSubmissions]'))
    CREATE INDEX [IX_RiskResponseSubmissions_RiskId] ON [risk_management].[RiskResponseSubmissions] ([RiskId]);
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
