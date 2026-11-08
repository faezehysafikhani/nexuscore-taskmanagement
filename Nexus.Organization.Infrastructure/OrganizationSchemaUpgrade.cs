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
        IF COL_LENGTH(N'organization.OrganizationUnits', N'NodeTypeId') IS NULL
            ALTER TABLE [organization].[OrganizationUnits] ADD [NodeTypeId] uniqueidentifier NULL;
        """,

        """
        IF COL_LENGTH(N'organization.OrganizationUnits', N'ParentUnitId') IS NULL
            ALTER TABLE [organization].[OrganizationUnits] ADD [ParentUnitId] uniqueidentifier NULL;
        """,

        """
        IF COL_LENGTH(N'organization.OrganizationUnits', N'ValidFrom') IS NULL
            ALTER TABLE [organization].[OrganizationUnits] ADD [ValidFrom] datetimeoffset NULL;
        """,

        """
        IF COL_LENGTH(N'organization.OrganizationUnits', N'ValidTo') IS NULL
            ALTER TABLE [organization].[OrganizationUnits] ADD [ValidTo] datetimeoffset NULL;
        """,

        """
        IF COL_LENGTH(N'organization.OrganizationUnits', N'SortOrder') IS NULL
            ALTER TABLE [organization].[OrganizationUnits] ADD [SortOrder] int NOT NULL CONSTRAINT [DF_OrganizationUnits_SortOrder] DEFAULT 0;
        """,

        """
        IF COL_LENGTH(N'organization.OrganizationUnits', N'Address') IS NULL
            ALTER TABLE [organization].[OrganizationUnits] ADD [Address] nvarchar(500) NULL;
        """,

        """
        IF COL_LENGTH(N'organization.OrganizationUnits', N'CorrespondenceTitle') IS NULL
            ALTER TABLE [organization].[OrganizationUnits] ADD [CorrespondenceTitle] nvarchar(200) NULL;
        """,

        """
        IF COL_LENGTH(N'organization.OrganizationUnits', N'LatinName') IS NULL
            ALTER TABLE [organization].[OrganizationUnits] ADD [LatinName] nvarchar(200) NULL;
        """,

        """
        IF COL_LENGTH(N'organization.OrganizationUnits', N'NodeCode') IS NULL
            ALTER TABLE [organization].[OrganizationUnits] ADD [NodeCode] nvarchar(40) NULL;
        """,

        """
        IF COL_LENGTH(N'organization.OrganizationUnits', N'Phone') IS NULL
            ALTER TABLE [organization].[OrganizationUnits] ADD [Phone] nvarchar(40) NULL;
        """,

        """
        IF COL_LENGTH(N'organization.OrganizationUnits', N'RowVersion') IS NULL
            ALTER TABLE [organization].[OrganizationUnits] ADD [RowVersion] rowversion;
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OrganizationUnits_NodeTypeId' AND object_id = OBJECT_ID(N'[organization].[OrganizationUnits]'))
            CREATE INDEX [IX_OrganizationUnits_NodeTypeId] ON [organization].[OrganizationUnits] ([NodeTypeId]);
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OrganizationUnits_ParentUnitId' AND object_id = OBJECT_ID(N'[organization].[OrganizationUnits]'))
            CREATE INDEX [IX_OrganizationUnits_ParentUnitId] ON [organization].[OrganizationUnits] ([ParentUnitId]);
        """,

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
