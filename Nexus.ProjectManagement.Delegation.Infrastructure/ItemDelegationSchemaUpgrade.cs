using Microsoft.EntityFrameworkCore;

namespace Nexus.ProjectManagement.Delegation.Infrastructure;

/// <summary>
/// Creates the item-delegation schema on a deployment that predates the module. A host that runs
/// ModuleSchemaInitializer.EnsureCreatedAsync for ItemDelegationDbContext already gets it, because the schema is
/// new; this exists for hosts that prefer to call code over running docs/upgrade/2026-10-15-add-item-delegation.sql,
/// which does the same thing. It is a no-op once the table exists.
/// </summary>
public static class ItemDelegationSchemaUpgrade
{
    // Run one at a time (each is its own batch, as the GO-separated script is), in this order, in
    // one transaction. Kept identical to the DDL EF generates for the model; a test compares them.
    internal static readonly string[] Statements =
    [
        "IF SCHEMA_ID(N'item_delegation') IS NULL EXEC(N'CREATE SCHEMA [item_delegation];');",

        """
        IF OBJECT_ID(N'[item_delegation].[ItemDelegations]', N'U') IS NULL
            CREATE TABLE [item_delegation].[ItemDelegations] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [ItemType] int NOT NULL,
                [ItemId] uniqueidentifier NOT NULL,
                [ProjectId] uniqueidentifier NULL,
                [DelegatorUserId] uniqueidentifier NOT NULL,
                [DelegateUserId] uniqueidentifier NOT NULL,
                [IsActive] bit NOT NULL,
                [EndedAtUtc] datetimeoffset NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_ItemDelegations] PRIMARY KEY ([Id])
            );
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ItemDelegations_TenantId_ItemType_ItemId_IsActive' AND object_id = OBJECT_ID(N'[item_delegation].[ItemDelegations]'))
            CREATE INDEX [IX_ItemDelegations_TenantId_ItemType_ItemId_IsActive] ON [item_delegation].[ItemDelegations] ([TenantId], [ItemType], [ItemId], [IsActive]);
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ItemDelegations_TenantId_DelegatorUserId' AND object_id = OBJECT_ID(N'[item_delegation].[ItemDelegations]'))
            CREATE INDEX [IX_ItemDelegations_TenantId_DelegatorUserId] ON [item_delegation].[ItemDelegations] ([TenantId], [DelegatorUserId]);
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ItemDelegations_TenantId_DelegateUserId' AND object_id = OBJECT_ID(N'[item_delegation].[ItemDelegations]'))
            CREATE INDEX [IX_ItemDelegations_TenantId_DelegateUserId] ON [item_delegation].[ItemDelegations] ([TenantId], [DelegateUserId]);
        """
    ];

    public static async Task EnsureCurrentAsync(DbContext delegationDbContext, CancellationToken cancellationToken)
    {
        await using var transaction = await delegationDbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var statement in Statements)
        {
            await delegationDbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
