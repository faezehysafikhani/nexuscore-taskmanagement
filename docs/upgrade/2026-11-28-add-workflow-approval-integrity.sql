/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) for approval integrity in the Workflow
    module (project charter approval fixes H5/H6):
      1. adds the NULLABLE column [workflow].[WorkflowInstances].[SubmittedByUserId] - who sent the subject for approval,
         so a project's submitter cannot approve or reject their own charter;
      2. adds the NULLABLE column [workflow].[WorkflowInstances].[RowVersion] - a concurrency token the application
         changes on every decision, so two decisions on the same step at the same time cannot both be saved (the
         second gets 409 workflow.concurrent_decision).

    Existing approvals get NULL in both: they keep working exactly as before (the first decision on one sets its
    RowVersion; an approval with no recorded submitter has no self-approval check). Nothing is changed or dropped.
    The new instance status 3 (Withdrawn) needs no schema change (Status is an int column).

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never adds a column to a
    table that already exists. A host that prefers code can call
    Nexus.Workflow.Infrastructure.WorkflowApprovalIntegritySchemaUpgrade.EnsureCurrentAsync on startup instead (the
    platform bundle does); it runs the same statements in the same order. Safe to run more than once.

    Apply this BEFORE deploying the build that contains it: that build selects the new columns, so a table without
    them fails every workflow query. To undo: 2026-11-28-add-workflow-approval-integrity-rollback.sql.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-11-28-add-workflow-approval-integrity.sql
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

IF COL_LENGTH(N'workflow.WorkflowInstances', N'SubmittedByUserId') IS NULL
    ALTER TABLE [workflow].[WorkflowInstances] ADD [SubmittedByUserId] uniqueidentifier NULL;
GO

IF COL_LENGTH(N'workflow.WorkflowInstances', N'RowVersion') IS NULL
    ALTER TABLE [workflow].[WorkflowInstances] ADD [RowVersion] uniqueidentifier NULL;
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
