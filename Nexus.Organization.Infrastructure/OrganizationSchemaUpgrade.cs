using Microsoft.EntityFrameworkCore;

namespace Nexus.Organization.Infrastructure;

/// <summary>
/// Adds the unit-membership table to an existing deployment's organisation schema. ModuleSchemaInitializer.EnsureCreatedAsync
/// never adds a table to a schema that already exists. A host that runs EnsureCreatedAsync for this module can call this
/// right after it on every start; it is a no-op once the table exists. Hosts that manage the schema by script apply
/// docs/upgrade/2026-10-10-add-organization-members.sql, which does the same thing.
/// </summary>
public static class OrganizationSchemaUpgrade
{
    // Run one at a time (each its own batch, as the GO-separated script is), in this order, in one transaction.
    internal static readonly string[] Statements =
    [
        """
        IF OBJECT_ID(N'[organization].[OrganizationUnitMembers]', N'U') IS NULL
            CREATE TABLE [organization].[OrganizationUnitMembers] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [UnitId] uniqueidentifier NOT NULL,
                [UserId] uniqueidentifier NOT NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_OrganizationUnitMembers] PRIMARY KEY ([Id])
            );
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OrganizationUnitMembers_TenantId_UserId' AND object_id = OBJECT_ID(N'[organization].[OrganizationUnitMembers]'))
            CREATE UNIQUE INDEX [IX_OrganizationUnitMembers_TenantId_UserId] ON [organization].[OrganizationUnitMembers] ([TenantId], [UserId]);
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OrganizationUnitMembers_UnitId' AND object_id = OBJECT_ID(N'[organization].[OrganizationUnitMembers]'))
            CREATE INDEX [IX_OrganizationUnitMembers_UnitId] ON [organization].[OrganizationUnitMembers] ([UnitId]);
        """
    ];

    public static async Task EnsureCurrentAsync(DbContext organizationDbContext, CancellationToken cancellationToken)
    {
        await using var transaction = await organizationDbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var statement in Statements)
        {
            await organizationDbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
