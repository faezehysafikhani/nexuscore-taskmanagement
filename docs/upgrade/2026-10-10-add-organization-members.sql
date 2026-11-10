/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) so people can be placed in the
    organisation chart: each user belongs to at most one organisation unit.

    Creates [organization].[OrganizationUnitMembers] (unit + user, unique per tenant and user) and its indexes.
    Nothing existing is changed or dropped; nobody is placed in the chart until an administrator does it.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never adds a table to a
    schema that already exists. A host that prefers code can call
    Nexus.Organization.Infrastructure.OrganizationSchemaUpgrade.EnsureCurrentAsync on startup instead; it runs the same
    statements in the same order. Safe to run more than once.

    Apply this BEFORE deploying the build that contains it.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-10-add-organization-members.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Skipped, without an error, on a database that does not have this module's tables yet: the module creates
-- its whole schema (this change included) the first time the host starts against such a database, and a
-- partial schema made here would stop it from doing so.
IF OBJECT_ID(N'[organization].[OrganizationUnits]', N'U') IS NULL
BEGIN
    PRINT N'Skipped: Organization is not installed in this database ([organization].[OrganizationUnits] does not exist). Nothing to upgrade.';
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;
GO

IF COL_LENGTH(N'organization.OrganizationUnits', N'NodeTypeId') IS NULL
    ALTER TABLE [organization].[OrganizationUnits] ADD [NodeTypeId] uniqueidentifier NULL;
GO

IF COL_LENGTH(N'organization.OrganizationUnits', N'ParentUnitId') IS NULL
    ALTER TABLE [organization].[OrganizationUnits] ADD [ParentUnitId] uniqueidentifier NULL;
GO

IF COL_LENGTH(N'organization.OrganizationUnits', N'ValidFrom') IS NULL
    ALTER TABLE [organization].[OrganizationUnits] ADD [ValidFrom] datetimeoffset NULL;
GO

IF COL_LENGTH(N'organization.OrganizationUnits', N'ValidTo') IS NULL
    ALTER TABLE [organization].[OrganizationUnits] ADD [ValidTo] datetimeoffset NULL;
GO

IF COL_LENGTH(N'organization.OrganizationUnits', N'SortOrder') IS NULL
    ALTER TABLE [organization].[OrganizationUnits] ADD [SortOrder] int NOT NULL CONSTRAINT [DF_OrganizationUnits_SortOrder] DEFAULT 0;
GO

IF COL_LENGTH(N'organization.OrganizationUnits', N'Address') IS NULL
    ALTER TABLE [organization].[OrganizationUnits] ADD [Address] nvarchar(500) NULL;
GO

IF COL_LENGTH(N'organization.OrganizationUnits', N'CorrespondenceTitle') IS NULL
    ALTER TABLE [organization].[OrganizationUnits] ADD [CorrespondenceTitle] nvarchar(200) NULL;
GO

IF COL_LENGTH(N'organization.OrganizationUnits', N'LatinName') IS NULL
    ALTER TABLE [organization].[OrganizationUnits] ADD [LatinName] nvarchar(200) NULL;
GO

IF COL_LENGTH(N'organization.OrganizationUnits', N'NodeCode') IS NULL
    ALTER TABLE [organization].[OrganizationUnits] ADD [NodeCode] nvarchar(40) NULL;
GO

IF COL_LENGTH(N'organization.OrganizationUnits', N'Phone') IS NULL
    ALTER TABLE [organization].[OrganizationUnits] ADD [Phone] nvarchar(40) NULL;
GO

IF COL_LENGTH(N'organization.OrganizationUnits', N'RowVersion') IS NULL
    ALTER TABLE [organization].[OrganizationUnits] ADD [RowVersion] rowversion;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OrganizationUnits_NodeTypeId' AND object_id = OBJECT_ID(N'[organization].[OrganizationUnits]'))
    CREATE INDEX [IX_OrganizationUnits_NodeTypeId] ON [organization].[OrganizationUnits] ([NodeTypeId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OrganizationUnits_ParentUnitId' AND object_id = OBJECT_ID(N'[organization].[OrganizationUnits]'))
    CREATE INDEX [IX_OrganizationUnits_ParentUnitId] ON [organization].[OrganizationUnits] ([ParentUnitId]);
GO

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
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OrganizationUnitMembers_TenantId_UserId' AND object_id = OBJECT_ID(N'[organization].[OrganizationUnitMembers]'))
    CREATE UNIQUE INDEX [IX_OrganizationUnitMembers_TenantId_UserId] ON [organization].[OrganizationUnitMembers] ([TenantId], [UserId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OrganizationUnitMembers_UnitId' AND object_id = OBJECT_ID(N'[organization].[OrganizationUnitMembers]'))
    CREATE INDEX [IX_OrganizationUnitMembers_UnitId] ON [organization].[OrganizationUnitMembers] ([UnitId]);
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
