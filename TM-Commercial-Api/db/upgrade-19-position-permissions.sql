-- Permissions given by positions and posts (org structure): two new tables, org_structure.PositionPermissions (what a
-- position (سِمَت) gives to everyone who holds a post of it) and org_structure.UnitPositionPermissions (what one post
-- gives to its holder), each row a permission name. Both start empty: no row is added, changed or removed anywhere
-- else, so nobody's access changes until someone chooses permissions for a position or a post. Generated from the EF
-- Core migration AddPositionPermissions (Nexus.OrgStructure.Infrastructure). Idempotent: safe to run again. For a
-- database already upgraded with 1 to 18.
-- Not needed when Database:InitializeOrgStructureSchema is true (the default): the API does the same.
--   sqlcmd -S <server> -d TMPB -I -f 65001 -i db/upgrade-19-position-permissions.sql
-- Back up the database first. To undo: db/upgrade-19-rollback.sql.
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107071228_AddPositionPermissions'
)
BEGIN
    CREATE TABLE [org_structure].[PositionPermissions] (
        [CompanyId] uniqueidentifier NOT NULL,
        [PositionId] uniqueidentifier NOT NULL,
        [PermissionName] nvarchar(200) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_PositionPermissions] PRIMARY KEY ([CompanyId], [PositionId], [PermissionName])
    );
    CREATE TABLE [org_structure].[UnitPositionPermissions] (
        [UnitPositionId] uniqueidentifier NOT NULL,
        [PermissionName] nvarchar(200) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_UnitPositionPermissions] PRIMARY KEY ([UnitPositionId], [PermissionName])
    );
    CREATE INDEX [IX_PositionPermissions_TenantId] ON [org_structure].[PositionPermissions] ([TenantId]);
    CREATE INDEX [IX_UnitPositionPermissions_TenantId] ON [org_structure].[UnitPositionPermissions] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107071228_AddPositionPermissions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261107071228_AddPositionPermissions', N'8.0.24');
END;
GO

COMMIT;
GO
