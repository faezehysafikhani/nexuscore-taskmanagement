/*
    Rolls back 2026-11-28-add-workflow-approval-integrity.sql: drops [workflow].[WorkflowInstances].[SubmittedByUserId]
    and [RowVersion]. Who submitted each approval is lost (the self-approval check then has nothing to compare), and
    approvals taken back (Status = 3, Withdrawn) are turned into rejections (Status = 2) so the older build, which knows
    no status 3, reads them as closed. Use it only together with a build from before this change. Does nothing when the
    upgrade is not applied. Back up the database first.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-11-28-add-workflow-approval-integrity-rollback.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'[workflow].[WorkflowInstances]', N'U') IS NULL
BEGIN
    PRINT N'Skipped: Workflow is not installed in this database. Nothing to roll back.';
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;
GO

UPDATE [workflow].[WorkflowInstances] SET [Status] = 2 WHERE [Status] = 3;
GO

IF COL_LENGTH(N'workflow.WorkflowInstances', N'RowVersion') IS NOT NULL
    ALTER TABLE [workflow].[WorkflowInstances] DROP COLUMN [RowVersion];
GO

IF COL_LENGTH(N'workflow.WorkflowInstances', N'SubmittedByUserId') IS NOT NULL
    ALTER TABLE [workflow].[WorkflowInstances] DROP COLUMN [SubmittedByUserId];
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
