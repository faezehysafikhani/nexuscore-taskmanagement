/*
    Create the raci schema on an EXISTING NexusCore database (the DefaultConnection database)
    for the optional Nexus.ProjectManagement.Raci module: the RACI matrix of a project (who is
    Responsible, Accountable, Consulted or Informed for an activity, a deliverable or a task).

    What it does, in order:
      1. creates the [raci] schema;
      2. creates [raci].[RaciAssignments];
      3. creates the two indexes.

    Nothing existing is changed or dropped. The module starts empty, and a host that does not
    install the module never needs this script.

    Why a script: a host that runs ModuleSchemaInitializer.EnsureCreatedAsync for RaciDbContext
    (Rozet.Api does) gets all of this on its own the first time it starts, because the schema is new.
    Safe to run more than once, and safe to run after EnsureCreatedAsync has already created the table.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-20-add-raci.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
GO

IF SCHEMA_ID(N'raci') IS NULL EXEC(N'CREATE SCHEMA [raci];');
GO

IF OBJECT_ID(N'[raci].[RaciAssignments]', N'U') IS NULL
    CREATE TABLE [raci].[RaciAssignments] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [ItemType] int NOT NULL,
        [ItemId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Role] int NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_RaciAssignments] PRIMARY KEY ([Id])
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RaciAssignments_ItemType_ItemId_UserId_Role' AND object_id = OBJECT_ID(N'[raci].[RaciAssignments]'))
    CREATE UNIQUE INDEX [IX_RaciAssignments_ItemType_ItemId_UserId_Role] ON [raci].[RaciAssignments] ([ItemType], [ItemId], [UserId], [Role]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RaciAssignments_TenantId_ProjectId' AND object_id = OBJECT_ID(N'[raci].[RaciAssignments]'))
    CREATE INDEX [IX_RaciAssignments_TenantId_ProjectId] ON [raci].[RaciAssignments] ([TenantId], [ProjectId]);
GO

COMMIT TRANSACTION;
GO
