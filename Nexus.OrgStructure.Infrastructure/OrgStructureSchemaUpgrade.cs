using Microsoft.EntityFrameworkCore;

namespace Nexus.OrgStructure.Infrastructure;

/// <summary>
/// Adds the tables that came after a deployment's org_structure schema was first created (permissions from positions).
/// <c>ModuleSchemaInitializer.EnsureCreatedAsync</c> never adds a table to a schema that already exists, and a missing
/// table here breaks every sign-in (the user's permissions are read from it), so a host calls this on every start, also
/// with Database:SeedOnStartup off. Additive and idempotent; a no-op once the tables exist, or while the schema itself does not.
/// Hosts that manage the schema by script apply docs/upgrade/2026-11-08-add-position-permissions.sql instead.
/// </summary>
public static class OrgStructureSchemaUpgrade
{
    internal static readonly string[] Statements =
    [
        """
        IF SCHEMA_ID(N'org_structure') IS NOT NULL AND OBJECT_ID(N'[org_structure].[PositionPermissions]', N'U') IS NULL
            CREATE TABLE [org_structure].[PositionPermissions] (
                [CompanyId] uniqueidentifier NOT NULL,
                [PositionId] uniqueidentifier NOT NULL,
                [PermissionName] nvarchar(200) NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                CONSTRAINT [PK_PositionPermissions] PRIMARY KEY ([CompanyId], [PositionId], [PermissionName])
            );
        """,
        """
        IF SCHEMA_ID(N'org_structure') IS NOT NULL AND OBJECT_ID(N'[org_structure].[UnitPositionPermissions]', N'U') IS NULL
            CREATE TABLE [org_structure].[UnitPositionPermissions] (
                [UnitPositionId] uniqueidentifier NOT NULL,
                [PermissionName] nvarchar(200) NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                CONSTRAINT [PK_UnitPositionPermissions] PRIMARY KEY ([UnitPositionId], [PermissionName])
            );
        """,
        """
        IF OBJECT_ID(N'[org_structure].[PositionPermissions]', N'U') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PositionPermissions_TenantId' AND object_id = OBJECT_ID(N'[org_structure].[PositionPermissions]'))
            CREATE INDEX [IX_PositionPermissions_TenantId] ON [org_structure].[PositionPermissions] ([TenantId]);
        """,
        """
        IF OBJECT_ID(N'[org_structure].[UnitPositionPermissions]', N'U') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_UnitPositionPermissions_TenantId' AND object_id = OBJECT_ID(N'[org_structure].[UnitPositionPermissions]'))
            CREATE INDEX [IX_UnitPositionPermissions_TenantId] ON [org_structure].[UnitPositionPermissions] ([TenantId]);
        """
    ];

    public static async Task EnsureCurrentAsync(DbContext orgStructureDbContext, CancellationToken cancellationToken)
    {
        await using var transaction = await orgStructureDbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var statement in Statements)
        {
            await orgStructureDbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
