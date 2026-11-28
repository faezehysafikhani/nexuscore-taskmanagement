using Microsoft.EntityFrameworkCore;

namespace Nexus.Workflow.Infrastructure;

/// <summary>
/// Brings an existing deployment's workflow schema up to approval integrity: the SubmittedByUserId column (who sent the
/// subject, so they cannot approve it themselves) and the RowVersion concurrency token (two decisions on one step at
/// once cannot both be saved) on approvals. Both nullable: existing approvals keep working, the first decision sets
/// RowVersion. A no-op once they exist. Hosts that manage the schema by script apply
/// docs/upgrade/2026-11-28-add-workflow-approval-integrity.sql (undo: 2026-11-28-add-workflow-approval-integrity-rollback.sql).
/// </summary>
public static class WorkflowApprovalIntegritySchemaUpgrade
{
    // Run one at a time (each is its own batch, as the GO-separated script is), in this order, in
    // one transaction. Kept identical to the DDL EF generates for the model; a test compares them.
    internal static readonly string[] Statements =
    [
        """
        IF COL_LENGTH(N'workflow.WorkflowInstances', N'SubmittedByUserId') IS NULL
            ALTER TABLE [workflow].[WorkflowInstances] ADD [SubmittedByUserId] uniqueidentifier NULL;
        """,

        """
        IF COL_LENGTH(N'workflow.WorkflowInstances', N'RowVersion') IS NULL
            ALTER TABLE [workflow].[WorkflowInstances] ADD [RowVersion] uniqueidentifier NULL;
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
