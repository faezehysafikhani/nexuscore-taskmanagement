using Microsoft.EntityFrameworkCore;

namespace Nexus.Workflow.Infrastructure;

/// <summary>
/// Brings an existing deployment's workflow schema up to approver placeholders: the ApproverKey and
/// IsOptional columns on steps and the StepsSnapshot column on approvals. ModuleSchemaInitializer.EnsureCreatedAsync
/// never adds a column to a table that already exists, so they have to be patched in. A no-op once they exist.
/// Hosts that manage the schema by script apply docs/upgrade/2026-10-15-add-workflow-approver-placeholders.sql,
/// which does the same thing.
/// </summary>
public static class WorkflowApproverPlaceholdersSchemaUpgrade
{
    // Run one at a time (each is its own batch, as the GO-separated script is), in this order, in
    // one transaction. Kept identical to the DDL EF generates for the model; a test compares them.
    internal static readonly string[] Statements =
    [
        """
        IF COL_LENGTH(N'workflow.WorkflowSteps', N'ApproverKey') IS NULL
            ALTER TABLE [workflow].[WorkflowSteps] ADD [ApproverKey] nvarchar(40) NULL;
        """,

        """
        IF COL_LENGTH(N'workflow.WorkflowSteps', N'IsOptional') IS NULL
            ALTER TABLE [workflow].[WorkflowSteps] ADD [IsOptional] bit NOT NULL CONSTRAINT [DF_WorkflowSteps_IsOptional] DEFAULT CAST(0 AS bit);
        """,

        """
        IF COL_LENGTH(N'workflow.WorkflowInstances', N'StepsSnapshot') IS NULL
            ALTER TABLE [workflow].[WorkflowInstances] ADD [StepsSnapshot] nvarchar(max) NULL;
        """
    ];

    public static async Task EnsureCurrentAsync(DbContext workflowDbContext, CancellationToken cancellationToken)
    {
        await using var transaction = await workflowDbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var statement in Statements)
        {
            await workflowDbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
