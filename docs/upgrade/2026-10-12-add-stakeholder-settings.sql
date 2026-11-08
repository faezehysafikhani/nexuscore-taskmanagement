/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) for the stakeholder register's
    per-project engagement strategy wording: how a project words "what to do" for each of the four
    power x interest quadrants (manage closely, keep satisfied, keep informed, monitor).

    What it does, in order:
      1. creates [stakeholder_management].[StakeholderProjectSettings] (one optional row per project; a
         project without a row uses the default wording);
      2. creates its unique index on ProjectId.

    Nothing existing is changed or dropped.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never adds a
    table to a schema that already exists. A host that prefers code can call
    Nexus.ProjectManagement.StakeholderManagement.Infrastructure.StakeholderSchemaUpgrade.EnsureCurrentAsync
    on startup instead; it runs the same statements in the same order. Safe to run more than once.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-12-add-stakeholder-settings.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Skipped, without an error, on a database that does not have this module's tables yet: the module creates
-- its whole schema (this change included) the first time the host starts against such a database, and a
-- partial schema made here would stop it from doing so.
IF OBJECT_ID(N'[stakeholder_management].[Stakeholders]', N'U') IS NULL
BEGIN
    PRINT N'Skipped: Stakeholder management is not installed in this database ([stakeholder_management].[Stakeholders] does not exist). Nothing to upgrade.';
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;
GO

IF OBJECT_ID(N'[stakeholder_management].[StakeholderProjectSettings]', N'U') IS NULL
    CREATE TABLE [stakeholder_management].[StakeholderProjectSettings] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProjectId] uniqueidentifier NOT NULL,
        [ManageClosely] nvarchar(500) NULL,
        [KeepSatisfied] nvarchar(500) NULL,
        [KeepInformed] nvarchar(500) NULL,
        [Monitor] nvarchar(500) NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_StakeholderProjectSettings] PRIMARY KEY ([Id])
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StakeholderProjectSettings_ProjectId' AND object_id = OBJECT_ID(N'[stakeholder_management].[StakeholderProjectSettings]'))
    CREATE UNIQUE INDEX [IX_StakeholderProjectSettings_ProjectId] ON [stakeholder_management].[StakeholderProjectSettings] ([ProjectId]);
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
