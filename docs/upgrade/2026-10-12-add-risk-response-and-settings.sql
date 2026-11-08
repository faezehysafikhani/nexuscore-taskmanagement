/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) for the risk register's
    additions: how far a risk's response has got, and what a project says about its risks (the wording of
    the low / medium / high bands and the colour of each probability x impact cell).

    What it does, in order:
      1. adds [risk_management].[Risks].[ResponseStatus] int NOT NULL DEFAULT 0 (0 = Planning; every
         existing risk starts there);
      2. creates [risk_management].[RiskProjectSettings] (one optional row per project; a project without
         a row uses the defaults);
      3. creates its unique index on ProjectId.

    Nothing existing is changed or dropped.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never adds a
    table or a column to a schema that already exists. A host that prefers code can call
    Nexus.ProjectManagement.RiskManagement.Infrastructure.RiskSchemaUpgrade.EnsureCurrentAsync on startup
    instead; it runs the same statements in the same order. Safe to run more than once.

    Apply this BEFORE deploying the build that contains these features: that build selects the new column,
    so a Risks table without it fails every risk query.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-12-add-risk-response-and-settings.sql
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

IF COL_LENGTH(N'risk_management.Risks', N'ResponseStatus') IS NULL
    ALTER TABLE [risk_management].[Risks] ADD [ResponseStatus] int NOT NULL CONSTRAINT [DF_Risks_ResponseStatus] DEFAULT 0;
GO

IF OBJECT_ID(N'[risk_management].[RiskProjectSettings]', N'U') IS NULL
    CREATE TABLE [risk_management].[RiskProjectSettings] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [DefinitionsJson] nvarchar(max) NULL,
        [MatrixColorsJson] nvarchar(max) NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_RiskProjectSettings] PRIMARY KEY ([Id])
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RiskProjectSettings_ProjectId' AND object_id = OBJECT_ID(N'[risk_management].[RiskProjectSettings]'))
    CREATE UNIQUE INDEX [IX_RiskProjectSettings_ProjectId] ON [risk_management].[RiskProjectSettings] ([ProjectId]);
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
