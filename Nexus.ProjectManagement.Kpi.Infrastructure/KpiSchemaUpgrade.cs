using Microsoft.EntityFrameworkCore;

namespace Nexus.ProjectManagement.Kpi.Infrastructure;

/// <summary>
/// Brings an existing deployment's KPI schema up to the current model. The module's tables are created by
/// ModuleSchemaInitializer.EnsureCreatedAsync, which never adds a table to a schema that already exists - so the
/// measured values added later (KpiMeasurements) have to be patched in. A host that already runs EnsureCreatedAsync
/// for this module can call this right after it on every start; it is a no-op once the table exists. Hosts that
/// manage the schema by script instead apply docs/upgrade/2026-10-19-add-kpi-measurements.sql, which does the same thing.
/// </summary>
public static class KpiSchemaUpgrade
{
    // Run one at a time (each is its own batch, as the GO-separated script is), in this order, in
    // one transaction. Kept identical to the DDL EF generates for the model; a test compares them.
    internal static readonly string[] Statements =
    [
        "IF SCHEMA_ID(N'kpi') IS NULL EXEC(N'CREATE SCHEMA [kpi];');",

        """
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
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_KpiMeasurements_KpiDefinitionId_MeasuredOn' AND object_id = OBJECT_ID(N'[kpi].[KpiMeasurements]'))
            CREATE INDEX [IX_KpiMeasurements_KpiDefinitionId_MeasuredOn] ON [kpi].[KpiMeasurements] ([KpiDefinitionId], [MeasuredOn]);
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_KpiMeasurements_ProjectId' AND object_id = OBJECT_ID(N'[kpi].[KpiMeasurements]'))
            CREATE INDEX [IX_KpiMeasurements_ProjectId] ON [kpi].[KpiMeasurements] ([ProjectId]);
        """
    ];

    public static async Task EnsureCurrentAsync(DbContext kpiDbContext, CancellationToken cancellationToken)
    {
        await using var transaction = await kpiDbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var statement in Statements)
        {
            await kpiDbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
