-- Posts under posts for TMPB-API: the post above each post (org_structure.UnitPositions.ParentPostId),
-- whose holder manages the holder of the post below. Generated from the EF Core migration AddPostParent
-- (Nexus.OrgStructure.Infrastructure) in the NexusCore 0.1.0.1405072807-mahdi packages.
-- Idempotent: safe to run again. For a database already upgraded with 1 to 7 (run those first).
-- Not needed when Database:InitializeOrgStructureSchema is true (the default): the API does the same.
--   sqlcmd -S <server> -d TMPB -I -i db/upgrade-8-post-parent.sql
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020200132_AddPostParent'
)
BEGIN
    ALTER TABLE [org_structure].[UnitPositions] ADD [ParentPostId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020200132_AddPostParent'
)
BEGIN
    CREATE INDEX [IX_UnitPositions_ParentPostId] ON [org_structure].[UnitPositions] ([ParentPostId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020200132_AddPostParent'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261020200132_AddPostParent', N'8.0.24');
END;
GO

COMMIT;
GO

