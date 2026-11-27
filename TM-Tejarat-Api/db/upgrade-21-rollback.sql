-- Rollback of upgrade 21: removes the Workflow (approvals) tables and ALL their data (chains, approvals,
-- decisions, delegations). Tasks and timesheets stay; anything waiting for approval stays in its current state.
-- Back up first. Child tables before parents.
IF OBJECT_ID(N'[workflow].[WorkflowDecisions]', N'U') IS NOT NULL DROP TABLE [workflow].[WorkflowDecisions];
IF OBJECT_ID(N'[workflow].[WorkflowSteps]', N'U') IS NOT NULL DROP TABLE [workflow].[WorkflowSteps];
IF OBJECT_ID(N'[workflow].[WorkflowInstances]', N'U') IS NOT NULL DROP TABLE [workflow].[WorkflowInstances];
IF OBJECT_ID(N'[workflow].[WorkflowDelegations]', N'U') IS NOT NULL DROP TABLE [workflow].[WorkflowDelegations];
IF OBJECT_ID(N'[workflow].[WorkflowDefinitions]', N'U') IS NOT NULL DROP TABLE [workflow].[WorkflowDefinitions];
GO
IF SCHEMA_ID(N'workflow') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.objects WHERE schema_id = SCHEMA_ID(N'workflow'))
    EXEC(N'DROP SCHEMA [workflow];');
GO
