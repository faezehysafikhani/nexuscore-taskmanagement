/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) with the measured values of KPIs:
    a KPI definition says what is measured and its target; a measurement is one value of it on a date, so the
    dashboards can compare the KPI with its target and follow it over time.

    What it does, in order:
      1. creates [kpi].[KpiMeasurements] (one row per recorded value);
      2. creates its indexes.

    Nothing is changed or dropped. Existing KPI definitions keep working exactly as before: they simply have no
    values yet.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never adds a table to a
    schema that already exists. A host that prefers code can call
    Nexus.ProjectManagement.Kpi.Infrastructure.KpiSchemaUpgrade.EnsureCurrentAsync on startup instead; it runs the
    same statements in the same order. Safe to run more than once.

    Apply this BEFORE deploying the build that contains KPI measurements: that build reads the new table, so a KPI
    schema without it fails the measurement endpoints and the KPI dashboard (deleting a KPI as well).

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-19-add-kpi-measurements.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Skipped, without an error, on a database that does not have this module's tables yet: the module creates
-- its whole schema (this change included) the first time the host starts against such a database, and a
-- partial schema made here would stop it from doing so.
IF OBJECT_ID(N'[kpi].[KpiDefinitions]', N'U') IS NULL
BEGIN
    PRINT N'Skipped: KPIs are not installed in this database ([kpi].[KpiDefinitions] does not exist). Nothing to upgrade.';
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;
GO

IF SCHEMA_ID(N'kpi') IS NULL EXEC(N'CREATE SCHEMA [kpi];');
GO

IF OBJECT_ID(N'[kpi].[KpiMeasurements]', N'U') IS NULL
    CREATE TABLE [kpi].[KpiMeasurements] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [KpiDefinitionId] uniqueidentifier NOT NULL,
        [MeasuredOn] date NOT NULL,
        [Value] decimal(18,4) NOT NULL,
        [Note] nvarchar(1000) NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_KpiMeasurements] PRIMARY KEY ([Id])
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_KpiMeasurements_KpiDefinitionId_MeasuredOn' AND object_id = OBJECT_ID(N'[kpi].[KpiMeasurements]'))
    CREATE INDEX [IX_KpiMeasurements_KpiDefinitionId_MeasuredOn] ON [kpi].[KpiMeasurements] ([KpiDefinitionId], [MeasuredOn]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_KpiMeasurements_ProjectId' AND object_id = OBJECT_ID(N'[kpi].[KpiMeasurements]'))
    CREATE INDEX [IX_KpiMeasurements_ProjectId] ON [kpi].[KpiMeasurements] ([ProjectId]);
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
