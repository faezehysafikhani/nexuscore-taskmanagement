using Microsoft.EntityFrameworkCore;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.Actions.Infrastructure;

/// <summary>
/// Brings an existing deployment's Actions table up to the current model. The module's tables
/// are created by <see cref="ModuleSchemaInitializer.EnsureCreatedAsync"/>, which never adds a
/// column to a table that already exists - so a column added to the model later (Priority) has
/// to be patched in. A host that already runs EnsureCreatedAsync for this module can call this
/// right after it on every start; it is a no-op once the columns exist. Hosts that manage the
/// schema by script instead apply docs/upgrade/2026-10-03-add-action-priority.sql, which does
/// the same thing. The recurrence columns (RecurrenceUnit ... NextOccurrenceId) were added later
/// and are patched in the same way by docs/upgrade/2026-10-04-add-action-recurrence.sql.
/// </summary>
public static class ActionsSchemaUpgrade
{
    // Every column is nullable: an existing action is simply a one-off, so no default or back-fill is needed.
    internal static readonly (string Column, string AddSql)[] RecurrenceColumns =
    [
        ("RecurrenceUnit", "ALTER TABLE [actions].[Actions] ADD [RecurrenceUnit] int NULL;"),
        ("RecurrenceInterval", "ALTER TABLE [actions].[Actions] ADD [RecurrenceInterval] int NULL;"),
        ("RecurrenceEndDate", "ALTER TABLE [actions].[Actions] ADD [RecurrenceEndDate] date NULL;"),
        ("RecurrenceSourceId", "ALTER TABLE [actions].[Actions] ADD [RecurrenceSourceId] uniqueidentifier NULL;"),
        ("NextOccurrenceId", "ALTER TABLE [actions].[Actions] ADD [NextOccurrenceId] uniqueidentifier NULL;")
    ];

    public static async Task EnsureCurrentAsync(DbContext actionsDbContext, CancellationToken cancellationToken)
    {
        await ModuleSchemaInitializer.EnsureColumnAsync(
            actionsDbContext, "actions.Actions", "Priority",
            // 1 = ActionPriority.Normal, what every action was implicitly before the column existed.
            "ALTER TABLE [actions].[Actions] ADD [Priority] int NOT NULL CONSTRAINT [DF_Actions_Priority] DEFAULT 1;",
            addIndexSql: null, cancellationToken);

        foreach (var (column, addSql) in RecurrenceColumns)
        {
            await ModuleSchemaInitializer.EnsureColumnAsync(actionsDbContext, "actions.Actions", column, addSql, addIndexSql: null, cancellationToken);
        }
    }
}
