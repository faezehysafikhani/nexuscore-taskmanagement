/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) with approval
    delegation: an approver can hand their approvals to a substitute for a period, the substitute sees
    them in their Approval Center, and a decision made as the substitute records on whose behalf it was.

    What it does, in order:
      1. creates [workflow].[WorkflowDelegations] (who delegated to whom, which dates, optional subject type);
      2. adds the NULLABLE column [workflow].[WorkflowDecisions].[OnBehalfOfUserId];
      3. creates the two lookup indexes.

    Existing decisions get NULL in the new column (= "decided in their own right"), which is exactly
    what they were. Nothing is changed or dropped.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never adds a
    table or a column to a schema that already exists. A host that prefers code can call
    Nexus.Workflow.Infrastructure.WorkflowSchemaUpgrade.EnsureCurrentAsync on startup instead; it runs the
    same statements in the same order. Safe to run more than once.

    Apply this BEFORE deploying the build that contains delegation: that build selects the new column,
    so a WorkflowDecisions table without it fails every approval-center query.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-04-add-workflow-delegation.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Skipped, without an error, on a database that does not have this module's tables yet: the module creates
-- its whole schema (this change included) the first time the host starts against such a database, and a
-- partial schema made here would stop it from doing so.
IF OBJECT_ID(N'[workflow].[WorkflowInstances]', N'U') IS NULL
BEGIN
    PRINT N'Skipped: Workflow is not installed in this database ([workflow].[WorkflowInstances] does not exist). Nothing to upgrade.';
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;
GO

IF OBJECT_ID(N'[workflow].[WorkflowDelegations]', N'U') IS NULL
    CREATE TABLE [workflow].[WorkflowDelegations] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [DelegatorUserId] uniqueidentifier NOT NULL,
        [DelegateUserId] uniqueidentifier NOT NULL,
        [StartDate] date NOT NULL,
        [EndDate] date NOT NULL,
        [SubjectType] nvarchar(80) NULL,
        [Reason] nvarchar(500) NULL,
        [IsRevoked] bit NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_WorkflowDelegations] PRIMARY KEY ([Id])
    );
GO

IF COL_LENGTH(N'workflow.WorkflowDecisions', N'OnBehalfOfUserId') IS NULL
    ALTER TABLE [workflow].[WorkflowDecisions] ADD [OnBehalfOfUserId] uniqueidentifier NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowDelegations_TenantId_DelegateUserId' AND object_id = OBJECT_ID(N'[workflow].[WorkflowDelegations]'))
    CREATE INDEX [IX_WorkflowDelegations_TenantId_DelegateUserId] ON [workflow].[WorkflowDelegations] ([TenantId], [DelegateUserId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowDelegations_TenantId_DelegatorUserId' AND object_id = OBJECT_ID(N'[workflow].[WorkflowDelegations]'))
    CREATE INDEX [IX_WorkflowDelegations_TenantId_DelegatorUserId] ON [workflow].[WorkflowDelegations] ([TenantId], [DelegatorUserId]);
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
