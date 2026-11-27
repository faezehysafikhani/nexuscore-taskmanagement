-- Structure nodes for TMPB-API (dynamic org structure, phase 1.2): units get an optional parent, node type
-- and validity period (organization.OrganizationUnits), companies a group/template/screen level
-- (org_structure.Companies), posts the head's mark and the manager-post mark (org_structure.UnitPositions),
-- and the new tables NodeTypes, NodeTypeRules, NodeTypePostTemplates, LinkTypes, AutoLinkRules, OrgLinks,
-- StructureTemplates(+Items) and OrgChangeLogs. Generated from the EF Core migrations AddUnitNodeFields
-- (Nexus.Organization.Infrastructure) and AddOrgNodes (Nexus.OrgStructure.Infrastructure).
-- Today's data keeps its meaning: every organization gets the node types «شرکت» and «واحد»; its units become
-- «واحد» nodes with NO parent (flat, exactly as before); the head's post of each company gets the head's mark.
-- No rule is added. Row counts and Ids do not change (check with db/check-upgrade-10.sql before and after).
-- Idempotent: safe to run again. For a database already upgraded with 1 to 9 (run those first), and with the
-- TaskPostPeriods table (migration AddTaskPostPeriods) - the API creates it itself when it starts.
-- Not needed when Database:InitializeOrgStructureSchema is true (the default): the API does the same.
--   sqlcmd -S <server> -d TMPB -I -f 65001 -i db/upgrade-10-org-nodes.sql
-- Back up the database first. To undo: db/upgrade-10-rollback.sql (it deletes what the new columns/tables hold).
SET QUOTED_IDENTIFIER ON;
GO

-- Part 1/2: organization.OrganizationUnits (must run before part 2)
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181551_AddUnitNodeFields'
)
BEGIN
    ALTER TABLE [organization].[OrganizationUnits] ADD [NodeTypeId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181551_AddUnitNodeFields'
)
BEGIN
    ALTER TABLE [organization].[OrganizationUnits] ADD [ParentUnitId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181551_AddUnitNodeFields'
)
BEGIN
    ALTER TABLE [organization].[OrganizationUnits] ADD [RowVersion] rowversion NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181551_AddUnitNodeFields'
)
BEGIN
    ALTER TABLE [organization].[OrganizationUnits] ADD [ValidFrom] datetimeoffset NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181551_AddUnitNodeFields'
)
BEGIN
    ALTER TABLE [organization].[OrganizationUnits] ADD [ValidTo] datetimeoffset NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181551_AddUnitNodeFields'
)
BEGIN
    CREATE INDEX [IX_OrganizationUnits_NodeTypeId] ON [organization].[OrganizationUnits] ([NodeTypeId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181551_AddUnitNodeFields'
)
BEGIN
    CREATE INDEX [IX_OrganizationUnits_ParentUnitId] ON [organization].[OrganizationUnits] ([ParentUnitId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181551_AddUnitNodeFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261029181551_AddUnitNodeFields', N'8.0.24');
END;
GO

COMMIT;
GO


-- Part 2/2: org_structure
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    ALTER TABLE [org_structure].[UnitPositions] ADD [IsCompanyHead] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    ALTER TABLE [org_structure].[UnitPositions] ADD [IsManagerPost] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    ALTER TABLE [org_structure].[UnitPositions] ADD [RowVersion] rowversion NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    ALTER TABLE [org_structure].[Companies] ADD [ParentCompanyId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    ALTER TABLE [org_structure].[Companies] ADD [TemplateId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    ALTER TABLE [org_structure].[Companies] ADD [UiLevel] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    CREATE TABLE [org_structure].[AutoLinkRules] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [SourceNodeTypeId] uniqueidentifier NOT NULL,
        [TargetNodeTypeId] uniqueidentifier NOT NULL,
        [LinkTypeId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_AutoLinkRules] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    CREATE TABLE [org_structure].[LinkTypes] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Code] nvarchar(40) NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [CountsAsManager] bit NOT NULL,
        [LineStyle] nvarchar(20) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_LinkTypes] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    CREATE TABLE [org_structure].[NodeTypePostTemplates] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [NodeTypeId] uniqueidentifier NOT NULL,
        [PositionId] uniqueidentifier NOT NULL,
        [IsManagerPost] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_NodeTypePostTemplates] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    CREATE TABLE [org_structure].[NodeTypeRules] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ParentNodeTypeId] uniqueidentifier NULL,
        [ChildNodeTypeId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_NodeTypeRules] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    CREATE TABLE [org_structure].[NodeTypes] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Code] nvarchar(40) NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [IsDomainBoundary] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_NodeTypes] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    CREATE TABLE [org_structure].[OrgChangeLogs] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [EntityType] nvarchar(60) NOT NULL,
        [EntityId] uniqueidentifier NOT NULL,
        [Action] nvarchar(40) NOT NULL,
        [UserId] uniqueidentifier NULL,
        [AtUtc] datetimeoffset NOT NULL,
        [Details] nvarchar(max) NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrgChangeLogs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    CREATE TABLE [org_structure].[OrgLinks] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [FromUnitId] uniqueidentifier NOT NULL,
        [ToUnitId] uniqueidentifier NOT NULL,
        [LinkTypeId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrgLinks] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    CREATE TABLE [org_structure].[StructureTemplateItems] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [TemplateId] uniqueidentifier NOT NULL,
        [Key] nvarchar(60) NOT NULL,
        [ParentKey] nvarchar(60) NULL,
        [NodeTypeCode] nvarchar(40) NULL,
        [Name] nvarchar(200) NOT NULL,
        [SortOrder] int NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_StructureTemplateItems] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    CREATE TABLE [org_structure].[StructureTemplates] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Code] nvarchar(40) NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [IsSystem] bit NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_StructureTemplates] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    CREATE INDEX [IX_Companies_ParentCompanyId] ON [org_structure].[Companies] ([ParentCompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AutoLinkRules_TenantId_SourceNodeTypeId_TargetNodeTypeId_LinkTypeId] ON [org_structure].[AutoLinkRules] ([TenantId], [SourceNodeTypeId], [TargetNodeTypeId], [LinkTypeId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_LinkTypes_TenantId_Code] ON [org_structure].[LinkTypes] ([TenantId], [Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    CREATE INDEX [IX_NodeTypePostTemplates_NodeTypeId] ON [org_structure].[NodeTypePostTemplates] ([NodeTypeId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    CREATE INDEX [IX_NodeTypeRules_ChildNodeTypeId] ON [org_structure].[NodeTypeRules] ([ChildNodeTypeId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_NodeTypeRules_TenantId_ParentNodeTypeId_ChildNodeTypeId] ON [org_structure].[NodeTypeRules] ([TenantId], [ParentNodeTypeId], [ChildNodeTypeId]) WHERE [ParentNodeTypeId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_NodeTypes_TenantId_Code] ON [org_structure].[NodeTypes] ([TenantId], [Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    CREATE INDEX [IX_OrgChangeLogs_TenantId_EntityId_AtUtc] ON [org_structure].[OrgChangeLogs] ([TenantId], [EntityId], [AtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrgLinks_TenantId_FromUnitId_ToUnitId_LinkTypeId] ON [org_structure].[OrgLinks] ([TenantId], [FromUnitId], [ToUnitId], [LinkTypeId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    CREATE INDEX [IX_OrgLinks_ToUnitId] ON [org_structure].[OrgLinks] ([ToUnitId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StructureTemplateItems_TenantId_TemplateId_Key] ON [org_structure].[StructureTemplateItems] ([TenantId], [TemplateId], [Key]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StructureTemplates_TenantId_Code] ON [org_structure].[StructureTemplates] ([TenantId], [Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN

    CREATE TABLE #OrgNodeTenants (TenantId uniqueidentifier NOT NULL PRIMARY KEY);
    INSERT INTO #OrgNodeTenants SELECT DISTINCT [TenantId] FROM [org_structure].[Companies];
    IF OBJECT_ID(N'[organization].[OrganizationUnits]', N'U') IS NOT NULL
        EXEC(N'INSERT INTO #OrgNodeTenants SELECT DISTINCT u.[TenantId] FROM [organization].[OrganizationUnits] u WHERE NOT EXISTS (SELECT 1 FROM #OrgNodeTenants t WHERE t.[TenantId] = u.[TenantId])');

    INSERT INTO [org_structure].[NodeTypes] ([Id], [TenantId], [Code], [Name], [IsDomainBoundary], [SortOrder], [IsActive], [CreatedAtUtc])
    SELECT NEWID(), t.[TenantId], v.[Code], v.[Name], 1, v.[SortOrder], 1, SYSDATETIMEOFFSET()
    FROM #OrgNodeTenants t
    CROSS JOIN (VALUES (N'company', N'شرکت', 0), (N'unit', N'واحد', 1)) v([Code], [Name], [SortOrder])
    WHERE NOT EXISTS (SELECT 1 FROM [org_structure].[NodeTypes] n WHERE n.[TenantId] = t.[TenantId] AND n.[Code] = v.[Code]);

    IF COL_LENGTH(N'organization.OrganizationUnits', N'NodeTypeId') IS NOT NULL
        EXEC(N'UPDATE u SET u.[NodeTypeId] = n.[Id] FROM [organization].[OrganizationUnits] u JOIN [org_structure].[NodeTypes] n ON n.[TenantId] = u.[TenantId] AND n.[Code] = N''unit'' WHERE u.[NodeTypeId] IS NULL');

    UPDATE s SET s.[IsCompanyHead] = 1
    FROM [org_structure].[UnitPositions] s
    JOIN [org_structure].[Positions] p ON p.[Id] = s.[PositionId]
    WHERE s.[UnitId] IS NULL AND p.[ParentPositionId] IS NULL AND s.[IsCompanyHead] = 0
      AND EXISTS (SELECT 1 FROM [org_structure].[PositionCompanies] pc WHERE pc.[PositionId] = s.[PositionId] AND pc.[CompanyId] = s.[CompanyId]);

    DROP TABLE #OrgNodeTenants;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029181607_AddOrgNodes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261029181607_AddOrgNodes', N'8.0.24');
END;
GO

COMMIT;
GO

