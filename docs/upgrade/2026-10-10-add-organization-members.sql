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

BEGIN TRANSACTION;
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
