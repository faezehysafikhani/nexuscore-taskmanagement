-- Organization + OrgStructure schemas for a NEW TMPB database (no organization /
-- org_structure tables yet), generated from the NexusCore 0.1.0.1405072817-mahdi packages' current model.
-- Records every migration as applied, so later versions upgrade with the upgrade-*.sql scripts.
-- Run after the API has started once (the identity tables must exist), or after its core scripts.
--   sqlcmd -S <server> -d TMPB -I -i db/new-database-org-structure.sql
-- Not needed when Database:InitializeOrgStructureSchema is true (the default): the API does the same.
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'organization.OrganizationUnits', N'U') IS NOT NULL OR OBJECT_ID(N'org_structure.Companies', N'U') IS NOT NULL
BEGIN
    RAISERROR(N'The organization tables already exist: use the upgrade-*.sql scripts instead.', 16, 1);
    SET NOEXEC ON;
END;
GO

IF SCHEMA_ID(N'organization') IS NULL EXEC(N'CREATE SCHEMA [organization];');
GO


CREATE TABLE [organization].[OrganizationUnits] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [Name] nvarchar(160) NOT NULL,
    [ManagerUserId] uniqueidentifier NULL,
    [IsActive] bit NOT NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [CreatedByUserId] uniqueidentifier NULL,
    [ModifiedAtUtc] datetimeoffset NULL,
    [ModifiedByUserId] uniqueidentifier NULL,
    CONSTRAINT [PK_OrganizationUnits] PRIMARY KEY ([Id])
);
GO
GO

IF SCHEMA_ID(N'org_structure') IS NULL EXEC(N'CREATE SCHEMA [org_structure];');
GO


CREATE TABLE [org_structure].[Companies] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Code] nvarchar(40) NOT NULL,
    [Description] nvarchar(1000) NULL,
    [ManagerUserId] uniqueidentifier NULL,
    [IsActive] bit NOT NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [CreatedByUserId] uniqueidentifier NULL,
    [ModifiedAtUtc] datetimeoffset NULL,
    [ModifiedByUserId] uniqueidentifier NULL,
    CONSTRAINT [PK_Companies] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [org_structure].[CompanyUnits] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [UnitId] uniqueidentifier NOT NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [CreatedByUserId] uniqueidentifier NULL,
    [ModifiedAtUtc] datetimeoffset NULL,
    [ModifiedByUserId] uniqueidentifier NULL,
    CONSTRAINT [PK_CompanyUnits] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [org_structure].[Honorifics] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [Title] nvarchar(40) NOT NULL,
    [SortOrder] int NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [CreatedByUserId] uniqueidentifier NULL,
    [ModifiedAtUtc] datetimeoffset NULL,
    [ModifiedByUserId] uniqueidentifier NULL,
    CONSTRAINT [PK_Honorifics] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [org_structure].[Positions] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [Title] nvarchar(120) NOT NULL,
    [ParentPositionId] uniqueidentifier NULL,
    [IsActive] bit NOT NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [CreatedByUserId] uniqueidentifier NULL,
    [ModifiedAtUtc] datetimeoffset NULL,
    [ModifiedByUserId] uniqueidentifier NULL,
    CONSTRAINT [PK_Positions] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [org_structure].[UserAssignments] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [UnitId] uniqueidentifier NULL,
    [PositionId] uniqueidentifier NULL,
    [ManagerUserId] uniqueidentifier NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [CreatedByUserId] uniqueidentifier NULL,
    [ModifiedAtUtc] datetimeoffset NULL,
    [ModifiedByUserId] uniqueidentifier NULL,
    CONSTRAINT [PK_UserAssignments] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [org_structure].[UserHonorifics] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [HonorificId] uniqueidentifier NOT NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [CreatedByUserId] uniqueidentifier NULL,
    [ModifiedAtUtc] datetimeoffset NULL,
    [ModifiedByUserId] uniqueidentifier NULL,
    CONSTRAINT [PK_UserHonorifics] PRIMARY KEY ([Id])
);
GO


CREATE UNIQUE INDEX [IX_Companies_TenantId_Code] ON [org_structure].[Companies] ([TenantId], [Code]);
GO


CREATE INDEX [IX_CompanyUnits_CompanyId] ON [org_structure].[CompanyUnits] ([CompanyId]);
GO


CREATE UNIQUE INDEX [IX_CompanyUnits_TenantId_UnitId] ON [org_structure].[CompanyUnits] ([TenantId], [UnitId]);
GO


CREATE UNIQUE INDEX [IX_Honorifics_TenantId_Title] ON [org_structure].[Honorifics] ([TenantId], [Title]);
GO


CREATE INDEX [IX_Positions_ParentPositionId] ON [org_structure].[Positions] ([ParentPositionId]);
GO


CREATE UNIQUE INDEX [IX_Positions_TenantId_Title] ON [org_structure].[Positions] ([TenantId], [Title]);
GO


CREATE INDEX [IX_UserAssignments_CompanyId] ON [org_structure].[UserAssignments] ([CompanyId]);
GO


CREATE INDEX [IX_UserAssignments_ManagerUserId] ON [org_structure].[UserAssignments] ([ManagerUserId]);
GO


CREATE UNIQUE INDEX [IX_UserAssignments_TenantId_UserId] ON [org_structure].[UserAssignments] ([TenantId], [UserId]);
GO


CREATE INDEX [IX_UserAssignments_UnitId] ON [org_structure].[UserAssignments] ([UnitId]);
GO


CREATE INDEX [IX_UserHonorifics_HonorificId] ON [org_structure].[UserHonorifics] ([HonorificId]);
GO


CREATE UNIQUE INDEX [IX_UserHonorifics_TenantId_UserId] ON [org_structure].[UserHonorifics] ([TenantId], [UserId]);
GO
GO


CREATE TABLE [org_structure].[UnitPositions] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [UnitId] uniqueidentifier NULL,
    [PositionId] uniqueidentifier NOT NULL,
    [SortOrder] int NOT NULL,
    [Code] nvarchar(40) NOT NULL,
    [CompanyId] uniqueidentifier NOT NULL,
    [ParentPostId] uniqueidentifier NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [CreatedByUserId] uniqueidentifier NULL,
    [ModifiedAtUtc] datetimeoffset NULL,
    [ModifiedByUserId] uniqueidentifier NULL,
    CONSTRAINT [PK_UnitPositions] PRIMARY KEY ([Id])
);
GO


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
GO


CREATE INDEX [IX_UnitPositions_PositionId] ON [org_structure].[UnitPositions] ([PositionId]);
GO


CREATE UNIQUE INDEX [IX_UnitPositions_TenantId_CompanyId_Code] ON [org_structure].[UnitPositions] ([TenantId], [CompanyId], [Code]);
GO


CREATE INDEX [IX_UnitPositions_UnitId] ON [org_structure].[UnitPositions] ([UnitId]);
GO


CREATE INDEX [IX_UnitPositions_ParentPostId] ON [org_structure].[UnitPositions] ([ParentPostId]);
GO


CREATE UNIQUE INDEX [IX_UserPositions_TenantId_UnitPositionId] ON [org_structure].[UserPositions] ([TenantId], [UnitPositionId]);
GO


CREATE INDEX [IX_UserPositions_UserId] ON [org_structure].[UserPositions] ([UserId]);
GO


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
GO


CREATE TABLE [org_structure].[PostActings] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [VacantPostId] uniqueidentifier NOT NULL,
    [ActingPostId] uniqueidentifier NOT NULL,
    [AppliedUserId] uniqueidentifier NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [CreatedByUserId] uniqueidentifier NULL,
    [ModifiedAtUtc] datetimeoffset NULL,
    [ModifiedByUserId] uniqueidentifier NULL,
    CONSTRAINT [PK_PostActings] PRIMARY KEY ([Id])
);
GO


CREATE INDEX [IX_PostActings_ActingPostId] ON [org_structure].[PostActings] ([ActingPostId]);
GO


CREATE UNIQUE INDEX [IX_PostActings_TenantId_VacantPostId] ON [org_structure].[PostActings] ([TenantId], [VacantPostId]);
GO


CREATE TABLE [org_structure].[TaskPosts] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [TaskId] uniqueidentifier NOT NULL,
    [PostId] uniqueidentifier NOT NULL,
    [Role] int NOT NULL,
    [HolderUserId] uniqueidentifier NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [CreatedByUserId] uniqueidentifier NULL,
    [ModifiedAtUtc] datetimeoffset NULL,
    [ModifiedByUserId] uniqueidentifier NULL,
    CONSTRAINT [PK_TaskPosts] PRIMARY KEY ([Id])
);
GO


CREATE INDEX [IX_TaskPosts_PostId] ON [org_structure].[TaskPosts] ([PostId]);
GO


CREATE UNIQUE INDEX [IX_TaskPosts_TenantId_TaskId_PostId_Role] ON [org_structure].[TaskPosts] ([TenantId], [TaskId], [PostId], [Role]);
GO


CREATE TABLE [org_structure].[TaskPostPeriods] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [TaskId] uniqueidentifier NOT NULL,
    [PostId] uniqueidentifier NOT NULL,
    [Role] int NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [PostTitle] nvarchar(200) NOT NULL,
    [PostCode] nvarchar(40) NOT NULL,
    [Place] nvarchar(500) NOT NULL,
    [PositionId] uniqueidentifier NOT NULL,
    [PositionTitle] nvarchar(200) NOT NULL,
    [ActingPostId] uniqueidentifier NULL,
    [StartedAtUtc] datetimeoffset NOT NULL,
    [EndedAtUtc] datetimeoffset NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [CreatedByUserId] uniqueidentifier NULL,
    [ModifiedAtUtc] datetimeoffset NULL,
    [ModifiedByUserId] uniqueidentifier NULL,
    CONSTRAINT [PK_TaskPostPeriods] PRIMARY KEY ([Id])
);
GO


CREATE INDEX [IX_TaskPostPeriods_TenantId_PositionId] ON [org_structure].[TaskPostPeriods] ([TenantId], [PositionId]);
GO


CREATE INDEX [IX_TaskPostPeriods_TenantId_TaskId] ON [org_structure].[TaskPostPeriods] ([TenantId], [TaskId]);
GO


CREATE INDEX [IX_TaskPostPeriods_TenantId_UserId] ON [org_structure].[TaskPostPeriods] ([TenantId], [UserId]);
GO


CREATE INDEX [IX_PositionCompanies_CompanyId] ON [org_structure].[PositionCompanies] ([CompanyId]);
GO


CREATE UNIQUE INDEX [IX_PositionCompanies_TenantId_PositionId_CompanyId] ON [org_structure].[PositionCompanies] ([TenantId], [PositionId], [CompanyId]);
GO

GO

IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
    CREATE TABLE [__EFMigrationsHistory] ([MigrationId] nvarchar(150) NOT NULL, [ProductVersion] nvarchar(32) NOT NULL, CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId]));
GO

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261013111237_RemoveUnitCodeParent')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20261013111237_RemoveUnitCodeParent', N'8.0.0');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261013111847_Baseline_OrgStructure')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20261013111847_Baseline_OrgStructure', N'8.0.0');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261013112238_AddPositionParent')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20261013112238_AddPositionParent', N'8.0.0');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261013122348_RemovePositionCode')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20261013122348_RemovePositionCode', N'8.0.0');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261014111846_AddTeamsAndTitles')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20261014111846_AddTeamsAndTitles', N'8.0.0');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261014130145_RemoveOrgTeams')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20261014130145_RemoveOrgTeams', N'8.0.0');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261020142512_AddUnitPositions')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20261020142512_AddUnitPositions', N'8.0.0');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261020164434_AddPositionCompanies')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20261020164434_AddPositionCompanies', N'8.0.0');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261020181412_AddPosts')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20261020181412_AddPosts', N'8.0.0');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261020183212_AddTaskPosts')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20261020183212_AddTaskPosts', N'8.0.0');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261020185002_AddPostActings')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20261020185002_AddPostActings', N'8.0.0');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261020200132_AddPostParent')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20261020200132_AddPostParent', N'8.0.0');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261026104917_AddTaskPostPeriods')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20261026104917_AddTaskPostPeriods', N'8.0.0');
GO

SET NOEXEC OFF;
GO
