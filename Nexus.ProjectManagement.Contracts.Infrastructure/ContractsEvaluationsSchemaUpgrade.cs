using Microsoft.EntityFrameworkCore;

namespace Nexus.ProjectManagement.Contracts.Infrastructure;

/// <summary>
/// Brings an existing deployment's project-contracts schema up to the periodic evaluations of suppliers: the SupplierEvaluations table.
/// ModuleSchemaInitializer.EnsureCreatedAsync never adds a table next to tables that already exist, so it is applied here. A host that runs
/// EnsureCreatedAsync can call this right after it on every start; it is a no-op once done. Hosts that manage the schema by script apply
/// docs/upgrade/2026-10-19-add-supplier-evaluations.sql, which holds the same statements in the same order.
/// </summary>
public static class ContractsEvaluationsSchemaUpgrade
{
    internal static readonly string[] Statements =
    [
        """
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
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SupplierEvaluations_SupplierId' AND object_id = OBJECT_ID(N'[project_contracts].[SupplierEvaluations]'))
            CREATE INDEX [IX_SupplierEvaluations_SupplierId] ON [project_contracts].[SupplierEvaluations] ([SupplierId]);
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SupplierEvaluations_TenantId' AND object_id = OBJECT_ID(N'[project_contracts].[SupplierEvaluations]'))
            CREATE INDEX [IX_SupplierEvaluations_TenantId] ON [project_contracts].[SupplierEvaluations] ([TenantId]);
        """
    ];

    public static async Task EnsureCurrentAsync(DbContext contractsDbContext, CancellationToken cancellationToken)
    {
        await using var transaction = await contractsDbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var statement in Statements)
        {
            await contractsDbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
