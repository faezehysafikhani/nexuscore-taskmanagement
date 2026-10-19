/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) for the periodic evaluation of suppliers:
      - SupplierEvaluations: one assessment of a supplier (or of one of its contracts) for a period - four scores from 1 to 5
        (quality, timeliness, compliance with obligations, cooperation), their average, notes, and what the records said about
        the period when it was saved (milestones due and late, obligations due and missed).

    What it does: creates [project_contracts].[SupplierEvaluations] with an index on SupplierId and one on TenantId. Nothing is
    changed, dropped or updated, and running it again changes nothing.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never adds a table next to tables
    that already exist. A host that prefers code can call
    Nexus.ProjectManagement.Contracts.Infrastructure.ContractsEvaluationsSchemaUpgrade.EnsureCurrentAsync on startup instead; it runs the
    same statements in the same order.

    Apply this BEFORE deploying the build that contains it: that build reads the table for every supplier list.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-19-add-supplier-evaluations.sql
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

IF OBJECT_ID(N'[project_contracts].[SupplierEvaluations]', N'U') IS NULL
CREATE TABLE [project_contracts].[SupplierEvaluations] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [SupplierId] uniqueidentifier NOT NULL,
    [ContractId] uniqueidentifier NULL,
    [PeriodStart] date NOT NULL,
    [PeriodEnd] date NOT NULL,
    [QualityScore] int NOT NULL,
    [TimelinessScore] int NOT NULL,
    [ComplianceScore] int NOT NULL,
    [CooperationScore] int NOT NULL,
    [OverallScore] decimal(4,2) NOT NULL,
    [MilestonesInPeriod] int NOT NULL,
    [LateMilestones] int NOT NULL,
    [ObligationsInPeriod] int NOT NULL,
    [MissedObligations] int NOT NULL,
    [Notes] nvarchar(2000) NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [CreatedByUserId] uniqueidentifier NULL,
    [ModifiedAtUtc] datetimeoffset NULL,
    [ModifiedByUserId] uniqueidentifier NULL,
    CONSTRAINT [PK_SupplierEvaluations] PRIMARY KEY ([Id])
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SupplierEvaluations_SupplierId' AND object_id = OBJECT_ID(N'[project_contracts].[SupplierEvaluations]'))
    CREATE INDEX [IX_SupplierEvaluations_SupplierId] ON [project_contracts].[SupplierEvaluations] ([SupplierId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SupplierEvaluations_TenantId' AND object_id = OBJECT_ID(N'[project_contracts].[SupplierEvaluations]'))
    CREATE INDEX [IX_SupplierEvaluations_TenantId] ON [project_contracts].[SupplierEvaluations] ([TenantId]);
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
