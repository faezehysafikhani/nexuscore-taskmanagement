/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) for the change requests of a project:
      - ChangeRequests: a formal request to change a project's scope, schedule, cost or quality, numbered per project, with its
        impact on scope (text), schedule (days) and cost (rials) written down before it is decided on, its status (draft, pending
        approval, approved, rejected, implemented, cancelled), when it was submitted and decided, and when it was carried out.

    What it does: creates [project_contracts].[ChangeRequests] with a unique index on (ProjectId, Number). Nothing is changed,
    dropped or updated, and running it again changes nothing.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never adds a table next to tables
    that already exist. A host that prefers code can call
    Nexus.ProjectManagement.Contracts.Infrastructure.ContractsChangeRequestsSchemaUpgrade.EnsureCurrentAsync on startup instead; it runs
    the same statements in the same order.

    Apply this BEFORE deploying the build that contains it: that build reads the table whenever a project is deleted.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-19-add-change-requests.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Skipped, without an error, on a database that does not have this module's tables yet: the module creates
-- its whole schema (this change included) the first time the host starts against such a database, and a
-- partial schema made here would stop it from doing so.
IF OBJECT_ID(N'[project_contracts].[Contracts]', N'U') IS NULL
BEGIN
    PRINT N'Skipped: Contracts is not installed in this database ([project_contracts].[Contracts] does not exist). Nothing to upgrade.';
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;
GO

IF OBJECT_ID(N'[project_contracts].[ChangeRequests]', N'U') IS NULL
CREATE TABLE [project_contracts].[ChangeRequests] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [ProjectId] uniqueidentifier NOT NULL,
    [Number] int NOT NULL,
    [Title] nvarchar(300) NOT NULL,
    [Description] nvarchar(4000) NULL,
    [Reason] nvarchar(2000) NULL,
    [Type] int NOT NULL,
    [Priority] int NOT NULL,
    [RequestedBy] nvarchar(200) NULL,
    [ContractId] uniqueidentifier NULL,
    [ScopeImpact] nvarchar(4000) NULL,
    [ScheduleImpactDays] int NOT NULL,
    [CostImpact] decimal(19,0) NOT NULL,
    [OtherImpact] nvarchar(2000) NULL,
    [Status] int NOT NULL,
    [SubmittedAtUtc] datetimeoffset NULL,
    [DecidedAtUtc] datetimeoffset NULL,
    [DecisionNote] nvarchar(2000) NULL,
    [ImplementedDate] date NULL,
    [OutcomeNote] nvarchar(2000) NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [CreatedByUserId] uniqueidentifier NULL,
    [ModifiedAtUtc] datetimeoffset NULL,
    [ModifiedByUserId] uniqueidentifier NULL,
    CONSTRAINT [PK_ChangeRequests] PRIMARY KEY ([Id])
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ChangeRequests_ProjectId_Number' AND object_id = OBJECT_ID(N'[project_contracts].[ChangeRequests]'))
    CREATE UNIQUE INDEX [IX_ChangeRequests_ProjectId_Number] ON [project_contracts].[ChangeRequests] ([ProjectId], [Number]);
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
