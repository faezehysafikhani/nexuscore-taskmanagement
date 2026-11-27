-- Positions per company for TMPB-API: each position is for one or more companies, and a
-- company's head is whoever holds its position with no superior. Generated from the EF Core
-- migration AddPositionCompanies in the NexusCore Nexus.OrgStructure.Infrastructure package
-- (0.1.0.1405072803-mahdi).
-- Also: each position gets the companies it is used in (and so do the positions above it), and
-- today's heads keep their companies through a new position «رئیس کل».
-- Idempotent: safe to run again. For a database already upgraded with 1, 2 and 3 (run those first).
-- Not needed when Database:InitializeOrgStructureSchema is true (the default): the API does the same.
--   sqlcmd -S <server> -d TMPB -I -i db/upgrade-4-position-companies.sql
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020164434_AddPositionCompanies'
)
BEGIN
    CREATE TABLE [org_structure].[PositionCompanies] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [PositionId] uniqueidentifier NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_PositionCompanies] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020164434_AddPositionCompanies'
)
BEGIN
    CREATE INDEX [IX_PositionCompanies_CompanyId] ON [org_structure].[PositionCompanies] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020164434_AddPositionCompanies'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PositionCompanies_TenantId_PositionId_CompanyId] ON [org_structure].[PositionCompanies] ([TenantId], [PositionId], [CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020164434_AddPositionCompanies'
)
BEGIN
    WITH used AS (
        SELECT a.[TenantId], a.[PositionId], a.[CompanyId]
        FROM [org_structure].[UserAssignments] a WHERE a.[PositionId] IS NOT NULL
        UNION
        SELECT s.[TenantId], s.[PositionId], cu.[CompanyId]
        FROM [org_structure].[UnitPositions] s
        JOIN [org_structure].[CompanyUnits] cu ON cu.[TenantId] = s.[TenantId] AND cu.[UnitId] = s.[UnitId]
    ),
    up AS (
        SELECT u.[TenantId], u.[PositionId], u.[CompanyId], 0 AS [Depth] FROM used u
        UNION ALL
        SELECT up.[TenantId], p.[ParentPositionId], up.[CompanyId], up.[Depth] + 1
        FROM up JOIN [org_structure].[Positions] p ON p.[Id] = up.[PositionId] AND p.[TenantId] = up.[TenantId]
        WHERE p.[ParentPositionId] IS NOT NULL AND up.[Depth] < 50
    )
    INSERT INTO [org_structure].[PositionCompanies] ([Id], [TenantId], [PositionId], [CompanyId], [CreatedAtUtc])
    SELECT NEWID(), d.[TenantId], d.[PositionId], d.[CompanyId], CAST(SYSUTCDATETIME() AS datetimeoffset)
    FROM (SELECT DISTINCT [TenantId], [PositionId], [CompanyId] FROM up) d
    WHERE EXISTS (SELECT 1 FROM [org_structure].[Positions] p WHERE p.[Id] = d.[PositionId] AND p.[TenantId] = d.[TenantId])
      AND EXISTS (SELECT 1 FROM [org_structure].[Companies] c WHERE c.[Id] = d.[CompanyId] AND c.[TenantId] = d.[TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020164434_AddPositionCompanies'
)
BEGIN
    DECLARE @now datetimeoffset = CAST(SYSUTCDATETIME() AS datetimeoffset);
    DECLARE @heads TABLE ([TenantId] uniqueidentifier, [CompanyId] uniqueidentifier, [UserId] uniqueidentifier, [Placed] bit, [Seq] int);

    INSERT INTO @heads
    SELECT c.[TenantId], c.[Id], c.[ManagerUserId], CASE WHEN a.[Id] IS NULL THEN 0 ELSE 1 END,
           ROW_NUMBER() OVER (PARTITION BY c.[TenantId], c.[ManagerUserId] ORDER BY c.[Name], c.[Id])
    FROM [org_structure].[Companies] c
    LEFT JOIN [org_structure].[UserAssignments] a ON a.[TenantId] = c.[TenantId] AND a.[UserId] = c.[ManagerUserId]
    WHERE c.[ManagerUserId] IS NOT NULL
      AND (a.[Id] IS NULL OR (a.[CompanyId] = c.[Id] AND a.[UnitId] IS NULL AND a.[PositionId] IS NULL));

    -- Someone with no place who heads several companies is placed in the first.
    DELETE FROM @heads WHERE [Placed] = 0 AND [Seq] > 1;

    INSERT INTO [org_structure].[Positions] ([Id], [TenantId], [Title], [ParentPositionId], [IsActive], [CreatedAtUtc])
    SELECT NEWID(), t.[TenantId], N'رئیس کل', NULL, 1, @now
    FROM (SELECT DISTINCT [TenantId] FROM @heads) t
    WHERE NOT EXISTS (SELECT 1 FROM [org_structure].[Positions] p WHERE p.[TenantId] = t.[TenantId] AND p.[Title] = N'رئیس کل');

    -- A «رئیس کل» that already has a superior is not the head's: those heads stay as they are.
    DELETE h FROM @heads h
    WHERE NOT EXISTS (SELECT 1 FROM [org_structure].[Positions] p
                      WHERE p.[TenantId] = h.[TenantId] AND p.[Title] = N'رئیس کل' AND p.[ParentPositionId] IS NULL);

    INSERT INTO [org_structure].[PositionCompanies] ([Id], [TenantId], [PositionId], [CompanyId], [CreatedAtUtc])
    SELECT NEWID(), h.[TenantId], p.[Id], h.[CompanyId], @now
    FROM @heads h
    JOIN [org_structure].[Positions] p ON p.[TenantId] = h.[TenantId] AND p.[Title] = N'رئیس کل'
    WHERE NOT EXISTS (SELECT 1 FROM [org_structure].[PositionCompanies] pc
                      WHERE pc.[TenantId] = h.[TenantId] AND pc.[PositionId] = p.[Id] AND pc.[CompanyId] = h.[CompanyId]);

    UPDATE a SET a.[PositionId] = p.[Id], a.[ModifiedAtUtc] = @now
    FROM [org_structure].[UserAssignments] a
    JOIN @heads h ON h.[TenantId] = a.[TenantId] AND h.[UserId] = a.[UserId] AND h.[Placed] = 1
    JOIN [org_structure].[Positions] p ON p.[TenantId] = h.[TenantId] AND p.[Title] = N'رئیس کل';

    INSERT INTO [org_structure].[UserAssignments] ([Id], [TenantId], [UserId], [CompanyId], [UnitId], [PositionId], [ManagerUserId], [CreatedAtUtc])
    SELECT NEWID(), h.[TenantId], h.[UserId], h.[CompanyId], NULL, p.[Id], NULL, @now
    FROM @heads h
    JOIN [org_structure].[Positions] p ON p.[TenantId] = h.[TenantId] AND p.[Title] = N'رئیس کل'
    WHERE h.[Placed] = 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020164434_AddPositionCompanies'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261020164434_AddPositionCompanies', N'8.0.24');
END;
GO

COMMIT;
GO

