-- Permissions from positions (org structure): two new tables, org_structure.PositionPermissions (a position's permissions in
-- one company) and org_structure.UnitPositionPermissions (a post's own permissions). Matches the EF Core migration
-- AddPositionPermissions (Nexus.OrgStructure.Infrastructure). No row is added, changed or removed in existing tables.
-- Idempotent: safe to run again. For a database already upgraded with the org-structure scripts up to upgrade-17.
-- Not needed on a database the host creates itself (Database:SeedOnStartup on, empty database).
IF OBJECT_ID(N'[org_structure].[PositionPermissions]', N'U') IS NULL
BEGIN
    CREATE TABLE [org_structure].[PositionPermissions] (
        [CompanyId] uniqueidentifier NOT NULL,
        [PositionId] uniqueidentifier NOT NULL,
        [PermissionName] nvarchar(200) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_PositionPermissions] PRIMARY KEY ([CompanyId], [PositionId], [PermissionName])
    );
END
GO

IF OBJECT_ID(N'[org_structure].[UnitPositionPermissions]', N'U') IS NULL
BEGIN
    CREATE TABLE [org_structure].[UnitPositionPermissions] (
        [UnitPositionId] uniqueidentifier NOT NULL,
        [PermissionName] nvarchar(200) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_UnitPositionPermissions] PRIMARY KEY ([UnitPositionId], [PermissionName])
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PositionPermissions_TenantId' AND object_id = OBJECT_ID(N'[org_structure].[PositionPermissions]'))
    CREATE INDEX [IX_PositionPermissions_TenantId] ON [org_structure].[PositionPermissions] ([TenantId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_UnitPositionPermissions_TenantId' AND object_id = OBJECT_ID(N'[org_structure].[UnitPositionPermissions]'))
    CREATE INDEX [IX_UnitPositionPermissions_TenantId] ON [org_structure].[UnitPositionPermissions] ([TenantId]);
GO
