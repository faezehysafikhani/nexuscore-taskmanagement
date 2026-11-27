-- Posts for TMPB-API: each post has a code (unique in its company) and one person; posts
-- can also sit directly in a company (the head's, board members'). Generated from the EF Core
-- migration AddPosts in the NexusCore Nexus.OrgStructure.Infrastructure package
-- (0.1.0.1405072804-mahdi).
-- Also: a position several people held in a deputy becomes one post each, people placed directly
-- in a company with a position get a post there, and every post gets a number of its company.
-- Idempotent: safe to run again. For a database already upgraded with 1 to 4 (run those first).
-- Not needed when Database:InitializeOrgStructureSchema is true (the default): the API does the same.
--   sqlcmd -S <server> -d TMPB -I -i db/upgrade-5-posts.sql
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020181412_AddPosts'
)
BEGIN
    DROP INDEX [IX_UserPositions_TenantId_UserId_UnitPositionId] ON [org_structure].[UserPositions];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020181412_AddPosts'
)
BEGIN
    DROP INDEX [IX_UserPositions_UnitPositionId] ON [org_structure].[UserPositions];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020181412_AddPosts'
)
BEGIN
    DROP INDEX [IX_UnitPositions_TenantId_UnitId_PositionId] ON [org_structure].[UnitPositions];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020181412_AddPosts'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[org_structure].[UnitPositions]') AND [c].[name] = N'UnitId');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [org_structure].[UnitPositions] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [org_structure].[UnitPositions] ALTER COLUMN [UnitId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020181412_AddPosts'
)
BEGIN
    ALTER TABLE [org_structure].[UnitPositions] ADD [Code] nvarchar(40) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020181412_AddPosts'
)
BEGIN
    ALTER TABLE [org_structure].[UnitPositions] ADD [CompanyId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020181412_AddPosts'
)
BEGIN
    DECLARE @now datetimeoffset = CAST(SYSUTCDATETIME() AS datetimeoffset);
    DECLARE @none uniqueidentifier = '00000000-0000-0000-0000-000000000000';

    UPDATE s SET s.[CompanyId] = cu.[CompanyId]
    FROM [org_structure].[UnitPositions] s
    JOIN [org_structure].[CompanyUnits] cu ON cu.[TenantId] = s.[TenantId] AND cu.[UnitId] = s.[UnitId];

    UPDATE s SET s.[CompanyId] = x.[CompanyId]
    FROM [org_structure].[UnitPositions] s
    CROSS APPLY (SELECT TOP 1 a.[CompanyId]
                 FROM [org_structure].[UserPositions] h
                 JOIN [org_structure].[UserAssignments] a ON a.[TenantId] = h.[TenantId] AND a.[UserId] = h.[UserId]
                 WHERE h.[UnitPositionId] = s.[Id]) x
    WHERE s.[CompanyId] = @none;

    DELETE h FROM [org_structure].[UserPositions] h
    JOIN [org_structure].[UnitPositions] s ON s.[Id] = h.[UnitPositionId]
    WHERE s.[CompanyId] = @none;
    DELETE FROM [org_structure].[UnitPositions] WHERE [CompanyId] = @none;

    DECLARE @split TABLE ([HoldingId] uniqueidentifier, [PostId] uniqueidentifier);
    INSERT INTO @split
    SELECT x.[Id], NEWID()
    FROM (SELECT h.[Id], ROW_NUMBER() OVER (PARTITION BY h.[UnitPositionId] ORDER BY h.[CreatedAtUtc], h.[Id]) AS [Rn]
          FROM [org_structure].[UserPositions] h) x
    WHERE x.[Rn] > 1;

    INSERT INTO [org_structure].[UnitPositions] ([Id], [TenantId], [CompanyId], [UnitId], [PositionId], [SortOrder], [Code], [CreatedAtUtc])
    SELECT sp.[PostId], s.[TenantId], s.[CompanyId], s.[UnitId], s.[PositionId], s.[SortOrder], N'', @now
    FROM @split sp
    JOIN [org_structure].[UserPositions] h ON h.[Id] = sp.[HoldingId]
    JOIN [org_structure].[UnitPositions] s ON s.[Id] = h.[UnitPositionId];

    UPDATE h SET h.[UnitPositionId] = sp.[PostId], h.[ModifiedAtUtc] = @now
    FROM [org_structure].[UserPositions] h
    JOIN @split sp ON sp.[HoldingId] = h.[Id];

    DECLARE @direct TABLE ([TenantId] uniqueidentifier, [UserId] uniqueidentifier, [CompanyId] uniqueidentifier,
                           [PositionId] uniqueidentifier, [IsRoot] bit, [PostId] uniqueidentifier);
    INSERT INTO @direct
    SELECT a.[TenantId], a.[UserId], a.[CompanyId], a.[PositionId],
           CASE WHEN p.[ParentPositionId] IS NULL THEN 1 ELSE 0 END, NEWID()
    FROM [org_structure].[UserAssignments] a
    JOIN [org_structure].[Positions] p ON p.[Id] = a.[PositionId] AND p.[TenantId] = a.[TenantId]
    WHERE a.[UnitId] IS NULL;

    INSERT INTO [org_structure].[UnitPositions] ([Id], [TenantId], [CompanyId], [UnitId], [PositionId], [SortOrder], [Code], [CreatedAtUtc])
    SELECT d.[PostId], d.[TenantId], d.[CompanyId], NULL, d.[PositionId], CASE WHEN d.[IsRoot] = 1 THEN 0 ELSE 1 END, N'', @now
    FROM @direct d;

    INSERT INTO [org_structure].[UserPositions] ([Id], [TenantId], [UserId], [UnitPositionId], [CreatedAtUtc])
    SELECT NEWID(), d.[TenantId], d.[UserId], d.[PostId], @now
    FROM @direct d;

    INSERT INTO [org_structure].[PositionCompanies] ([Id], [TenantId], [PositionId], [CompanyId], [CreatedAtUtc])
    SELECT NEWID(), x.[TenantId], x.[PositionId], x.[CompanyId], @now
    FROM (SELECT DISTINCT [TenantId], [PositionId], [CompanyId] FROM [org_structure].[UnitPositions]) x
    WHERE NOT EXISTS (SELECT 1 FROM [org_structure].[PositionCompanies] pc
                      WHERE pc.[TenantId] = x.[TenantId] AND pc.[PositionId] = x.[PositionId] AND pc.[CompanyId] = x.[CompanyId]);

    WITH numbered AS (
        SELECT s.[Id], ROW_NUMBER() OVER (
                   PARTITION BY s.[TenantId], s.[CompanyId]
                   ORDER BY CASE WHEN s.[UnitId] IS NULL THEN 0 ELSE 1 END, s.[UnitId], s.[SortOrder], p.[Title], s.[CreatedAtUtc], s.[Id]) AS [N]
        FROM [org_structure].[UnitPositions] s
        LEFT JOIN [org_structure].[Positions] p ON p.[Id] = s.[PositionId]
    )
    UPDATE s SET s.[Code] = CAST(n.[N] AS nvarchar(40))
    FROM [org_structure].[UnitPositions] s
    JOIN numbered n ON n.[Id] = s.[Id];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020181412_AddPosts'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UserPositions_TenantId_UnitPositionId] ON [org_structure].[UserPositions] ([TenantId], [UnitPositionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020181412_AddPosts'
)
BEGIN
    CREATE INDEX [IX_UserPositions_UserId] ON [org_structure].[UserPositions] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020181412_AddPosts'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UnitPositions_TenantId_CompanyId_Code] ON [org_structure].[UnitPositions] ([TenantId], [CompanyId], [Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020181412_AddPosts'
)
BEGIN
    CREATE INDEX [IX_UnitPositions_UnitId] ON [org_structure].[UnitPositions] ([UnitId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020181412_AddPosts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261020181412_AddPosts', N'8.0.24');
END;
GO

COMMIT;
GO

