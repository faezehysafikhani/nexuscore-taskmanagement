/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) with approver placeholders in
    the Workflow module: a step can name "the Manager / the Owner / Approver 1..3 of the thing being approved"
    instead of one fixed user, and every running approval keeps a copy of the steps (with the people they
    resolved to) it started with.

    What it does, in order:
      1. adds the NULLABLE column [workflow].[WorkflowSteps].[ApproverKey];
      2. adds [workflow].[WorkflowSteps].[IsOptional] (bit, default 0);
      3. adds the NULLABLE column [workflow].[WorkflowInstances].[StepsSnapshot].

    Existing steps get no placeholder and are not optional, and existing approvals get no snapshot (they keep
    reading their definition's steps) - which is exactly how they behaved. Nothing is changed or dropped.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never adds a
    column to a table that already exists. A host that prefers code can call
    Nexus.Workflow.Infrastructure.WorkflowSchemaUpgrade.EnsureCurrentAsync on startup instead; it runs the
    same statements in the same order. Safe to run more than once.

    Apply this BEFORE deploying the build that contains it: that build selects the new columns, so a
    table without them fails every workflow query.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-15-add-workflow-approver-placeholders.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Skipped, without an error, on a database that does not have this module's tables yet: the module creates
-- its whole schema (this change included) the first time the host starts against such a database.
IF OBJECT_ID(N'[workflow].[WorkflowInstances]', N'U') IS NULL
BEGIN
    PRINT N'Skipped: Workflow is not installed in this database ([workflow].[WorkflowInstances] does not exist). Nothing to upgrade.';
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;
GO

IF COL_LENGTH(N'workflow.WorkflowSteps', N'ApproverKey') IS NULL
    ALTER TABLE [workflow].[WorkflowSteps] ADD [ApproverKey] nvarchar(40) NULL;
GO

IF COL_LENGTH(N'workflow.WorkflowSteps', N'IsOptional') IS NULL
    ALTER TABLE [workflow].[WorkflowSteps] ADD [IsOptional] bit NOT NULL CONSTRAINT [DF_WorkflowSteps_IsOptional] DEFAULT CAST(0 AS bit);
GO

IF COL_LENGTH(N'workflow.WorkflowInstances', N'StepsSnapshot') IS NULL
    ALTER TABLE [workflow].[WorkflowInstances] ADD [StepsSnapshot] nvarchar(max) NULL;
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
