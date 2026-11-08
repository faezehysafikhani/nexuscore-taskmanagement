/*
    Create the project-history schema on an EXISTING NexusCore database (the DefaultConnection
    database) for the Nexus.ProjectManagement.History module: one row for every create, edit or
    delete of anything that belongs to a project (the project itself, risks, activities, documents,
    contracts...), with who did it, when, and the old and new value of each changed property.

    What it does, in order:
      1. creates the [project_history] schema;
      2. creates [project_history].[ProjectChanges];
      3. creates the (ProjectId, ChangedAtUtc) index that the history query uses.

    Nothing existing is changed or dropped. History starts empty: changes made before the module was
    installed are not reconstructed.

    Why a script: a host that already runs ModuleSchemaInitializer.EnsureCreatedAsync for
    ProjectHistoryDbContext gets all of this on its own the first time the module starts, because the
    schema is new. Hosts that manage the schema by script instead - or that want the table in place
    before the build is deployed - run this file. A host that prefers code can call
    Nexus.ProjectManagement.History.Infrastructure.ProjectHistorySchemaUpgrade.EnsureCurrentAsync; it runs
    the same statements in the same order. Safe to run more than once, and safe to run after
    EnsureCreatedAsync has already created the table.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-04-add-project-history.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
GO

IF SCHEMA_ID(N'project_history') IS NULL EXEC(N'CREATE SCHEMA [project_history];');
GO

IF OBJECT_ID(N'[project_history].[ProjectChanges]', N'U') IS NULL
    CREATE TABLE [project_history].[ProjectChanges] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [EntityName] nvarchar(100) NOT NULL,
        [EntityId] uniqueidentifier NULL,
        [Kind] int NOT NULL,
        [ChangedByUserId] uniqueidentifier NULL,
        [ChangedAtUtc] datetimeoffset NOT NULL,
        [ChangesJson] nvarchar(max) NULL,
        CONSTRAINT [PK_ProjectChanges] PRIMARY KEY ([Id])
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjectChanges_ProjectId_ChangedAtUtc' AND object_id = OBJECT_ID(N'[project_history].[ProjectChanges]'))
    CREATE INDEX [IX_ProjectChanges_ProjectId_ChangedAtUtc] ON [project_history].[ProjectChanges] ([ProjectId], [ChangedAtUtc]);
GO

COMMIT TRANSACTION;
GO
