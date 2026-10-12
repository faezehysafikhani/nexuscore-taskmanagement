using Microsoft.EntityFrameworkCore;

namespace Nexus.ProjectManagement.RiskManagement.Infrastructure;

/// <summary>
/// Brings an existing deployment's risk-management schema up to the current model: the risk response status column and the
/// per-project risk settings table. ModuleSchemaInitializer.EnsureCreatedAsync never adds a table or a column to a schema that
/// already exists, so they have to be patched in. A host that runs EnsureCreatedAsync for this module can call this right after
/// it on every start; it is a no-op once everything exists. Hosts that manage the schema by script apply
/// docs/upgrade/2026-10-12-add-risk-response-and-settings.sql, which does the same thing.
/// </summary>
public static class RiskSchemaUpgrade
{
    // Run one at a time (each is its own batch, as the GO-separated script is), in this order, in
    // one transaction. Kept identical to the DDL EF generates for the model; a test compares them.
    internal static readonly string[] Statements =
    [
        """
        IF COL_LENGTH(N'risk_management.Risks', N'ResponseStatus') IS NULL
            ALTER TABLE [risk_management].[Risks] ADD [ResponseStatus] int NOT NULL CONSTRAINT [DF_Risks_ResponseStatus] DEFAULT 0;
        """,

        """
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
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RiskProjectSettings_ProjectId' AND object_id = OBJECT_ID(N'[risk_management].[RiskProjectSettings]'))
            CREATE UNIQUE INDEX [IX_RiskProjectSettings_ProjectId] ON [risk_management].[RiskProjectSettings] ([ProjectId]);
        """
    ];

    public static async Task EnsureCurrentAsync(DbContext riskDbContext, CancellationToken cancellationToken)
    {
        await using var transaction = await riskDbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var statement in Statements)
        {
            await riskDbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
