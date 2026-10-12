using Microsoft.EntityFrameworkCore;

namespace Nexus.ProjectManagement.StakeholderManagement.Infrastructure;

/// <summary>
/// Brings an existing deployment's stakeholder-management schema up to the current model: the per-project engagement strategy
/// wording table. ModuleSchemaInitializer.EnsureCreatedAsync never adds a table to a schema that already exists, so it has to be
/// patched in. A host that runs EnsureCreatedAsync for this module can call this right after it on every start; it is a no-op once
/// everything exists. Hosts that manage the schema by script apply docs/upgrade/2026-10-12-add-stakeholder-settings.sql.
/// </summary>
public static class StakeholderSchemaUpgrade
{
    // Run one at a time (each is its own batch, as the GO-separated script is), in this order, in
    // one transaction. Kept identical to the DDL EF generates for the model; a test compares them.
    internal static readonly string[] Statements =
    [
        """
        IF OBJECT_ID(N'[stakeholder_management].[StakeholderProjectSettings]', N'U') IS NULL
            CREATE TABLE [stakeholder_management].[StakeholderProjectSettings] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [ProjectId] uniqueidentifier NOT NULL,
                [ManageClosely] nvarchar(500) NULL,
                [KeepSatisfied] nvarchar(500) NULL,
                [KeepInformed] nvarchar(500) NULL,
                [Monitor] nvarchar(500) NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_StakeholderProjectSettings] PRIMARY KEY ([Id])
            );
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StakeholderProjectSettings_ProjectId' AND object_id = OBJECT_ID(N'[stakeholder_management].[StakeholderProjectSettings]'))
            CREATE UNIQUE INDEX [IX_StakeholderProjectSettings_ProjectId] ON [stakeholder_management].[StakeholderProjectSettings] ([ProjectId]);
        """
    ];

    public static async Task EnsureCurrentAsync(DbContext stakeholderDbContext, CancellationToken cancellationToken)
    {
        await using var transaction = await stakeholderDbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var statement in Statements)
        {
            await stakeholderDbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
