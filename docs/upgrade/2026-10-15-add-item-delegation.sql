/*
    Create the item-delegation schema on an EXISTING NexusCore database (the DefaultConnection
    database) for the Nexus.ProjectManagement.Delegation module: the record of who handed an activity,
    a task or an action to whom, so a hand-over can be taken back and everything passed on after it ends with it.

    What it does, in order:
      1. creates the [item_delegation] schema;
      2. creates [item_delegation].[ItemDelegations];
      3. creates the three lookup indexes.

    Nothing existing is changed or dropped. The module starts empty.

    Why a script: a host that already runs ModuleSchemaInitializer.EnsureCreatedAsync for
    ItemDelegationDbContext gets all of this on its own the first time the module starts, because the
    schema is new. Hosts that manage the schema by script instead - or that want the table in place
    before the build is deployed - run this file. A host that prefers code can call
    Nexus.ProjectManagement.Delegation.Infrastructure.ItemDelegationSchemaUpgrade.EnsureCurrentAsync; it runs
    the same statements in the same order. Safe to run more than once, and safe to run after
    EnsureCreatedAsync has already created the table.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-15-add-item-delegation.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
GO

IF SCHEMA_ID(N'item_delegation') IS NULL EXEC(N'CREATE SCHEMA [item_delegation];');
GO

IF OBJECT_ID(N'[item_delegation].[ItemDelegations]', N'U') IS NULL
    CREATE TABLE [item_delegation].[ItemDelegations] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ItemType] int NOT NULL,
        [ItemId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NULL,
        [DelegatorUserId] uniqueidentifier NOT NULL,
        [DelegateUserId] uniqueidentifier NOT NULL,
        [IsActive] bit NOT NULL,
        [EndedAtUtc] datetimeoffset NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_ItemDelegations] PRIMARY KEY ([Id])
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ItemDelegations_TenantId_ItemType_ItemId_IsActive' AND object_id = OBJECT_ID(N'[item_delegation].[ItemDelegations]'))
    CREATE INDEX [IX_ItemDelegations_TenantId_ItemType_ItemId_IsActive] ON [item_delegation].[ItemDelegations] ([TenantId], [ItemType], [ItemId], [IsActive]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ItemDelegations_TenantId_DelegatorUserId' AND object_id = OBJECT_ID(N'[item_delegation].[ItemDelegations]'))
    CREATE INDEX [IX_ItemDelegations_TenantId_DelegatorUserId] ON [item_delegation].[ItemDelegations] ([TenantId], [DelegatorUserId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ItemDelegations_TenantId_DelegateUserId' AND object_id = OBJECT_ID(N'[item_delegation].[ItemDelegations]'))
    CREATE INDEX [IX_ItemDelegations_TenantId_DelegateUserId] ON [item_delegation].[ItemDelegations] ([TenantId], [DelegateUserId]);
GO

COMMIT TRANSACTION;
GO
