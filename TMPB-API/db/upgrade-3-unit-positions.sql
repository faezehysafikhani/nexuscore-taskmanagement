-- Unit (deputy) positions for TMPB-API: each deputy's own positions, in its order, and
-- who holds them (several per person). Generated from the EF Core migration AddUnitPositions in the
-- NexusCore Nexus.OrgStructure.Infrastructure package (0.1.0.1405072802-mahdi).
-- Also turns today's placements into deputy positions (each deputy gets the positions its people
-- have, and each person holds theirs).
-- Idempotent: safe to run again. For a database already upgraded with 1 and 2 (run those first).
-- Not needed when Database:InitializeOrgStructureSchema is true (the default): the API does the same.
--   sqlcmd -S <server> -d TMPB -I -i db/upgrade-3-unit-positions.sql
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020142512_AddUnitPositions'
)
BEGIN
    CREATE TABLE [org_structure].[UnitPositions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UnitId] uniqueidentifier NOT NULL,
        [PositionId] uniqueidentifier NOT NULL,
        [SortOrder] int NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UnitPositions] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020142512_AddUnitPositions'
)
BEGIN
    CREATE TABLE [org_structure].[UserPositions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [UnitPositionId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UserPositions] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020142512_AddUnitPositions'
)
BEGIN
    CREATE INDEX [IX_UnitPositions_PositionId] ON [org_structure].[UnitPositions] ([PositionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020142512_AddUnitPositions'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UnitPositions_TenantId_UnitId_PositionId] ON [org_structure].[UnitPositions] ([TenantId], [UnitId], [PositionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020142512_AddUnitPositions'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UserPositions_TenantId_UserId_UnitPositionId] ON [org_structure].[UserPositions] ([TenantId], [UserId], [UnitPositionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020142512_AddUnitPositions'
)
BEGIN
    CREATE INDEX [IX_UserPositions_UnitPositionId] ON [org_structure].[UserPositions] ([UnitPositionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020142512_AddUnitPositions'
)
BEGIN
    INSERT INTO [org_structure].[UnitPositions] ([Id], [TenantId], [UnitId], [PositionId], [SortOrder], [CreatedAtUtc])
    SELECT NEWID(), d.[TenantId], d.[UnitId], d.[PositionId],
           ROW_NUMBER() OVER (PARTITION BY d.[TenantId], d.[UnitId] ORDER BY p.[Title]) - 1,
           CAST(SYSUTCDATETIME() AS datetimeoffset)
    FROM (SELECT DISTINCT [TenantId], [UnitId], [PositionId] FROM [org_structure].[UserAssignments]
          WHERE [UnitId] IS NOT NULL AND [PositionId] IS NOT NULL) d
    JOIN [org_structure].[Positions] p ON p.[Id] = d.[PositionId];

    INSERT INTO [org_structure].[UserPositions] ([Id], [TenantId], [UserId], [UnitPositionId], [CreatedAtUtc])
    SELECT NEWID(), a.[TenantId], a.[UserId], s.[Id], CAST(SYSUTCDATETIME() AS datetimeoffset)
    FROM [org_structure].[UserAssignments] a
    JOIN [org_structure].[UnitPositions] s
      ON s.[TenantId] = a.[TenantId] AND s.[UnitId] = a.[UnitId] AND s.[PositionId] = a.[PositionId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020142512_AddUnitPositions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261020142512_AddUnitPositions', N'8.0.24');
END;
GO

COMMIT;
GO

