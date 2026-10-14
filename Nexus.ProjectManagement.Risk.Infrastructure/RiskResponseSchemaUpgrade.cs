using Microsoft.EntityFrameworkCore;

namespace Nexus.ProjectManagement.RiskManagement.Infrastructure;

/// <summary>
/// Brings an existing deployment's risk-management schema up to the current model for response reports: the table that holds a report of
/// where the work on a risk's response stands while it waits for verification. ModuleSchemaInitializer.EnsureCreatedAsync never adds a
/// table to a schema that already exists, so it has to be patched in. A host that runs EnsureCreatedAsync for this module can call this
/// right after it on every start; it is a no-op once the table exists. Hosts that manage the schema by script apply
/// docs/upgrade/2026-10-14-add-risk-response-submissions.sql, which does the same thing.
/// </summary>
public static class RiskResponseSchemaUpgrade
{
    // Run one at a time (each is its own batch, as the GO-separated script is), in this order, in one transaction.
    internal static readonly string[] Statements =
    [
        """
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
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RiskResponseSubmissions_ProjectId' AND object_id = OBJECT_ID(N'[risk_management].[RiskResponseSubmissions]'))
            CREATE INDEX [IX_RiskResponseSubmissions_ProjectId] ON [risk_management].[RiskResponseSubmissions] ([ProjectId]);
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RiskResponseSubmissions_RiskId' AND object_id = OBJECT_ID(N'[risk_management].[RiskResponseSubmissions]'))
            CREATE INDEX [IX_RiskResponseSubmissions_RiskId] ON [risk_management].[RiskResponseSubmissions] ([RiskId]);
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
