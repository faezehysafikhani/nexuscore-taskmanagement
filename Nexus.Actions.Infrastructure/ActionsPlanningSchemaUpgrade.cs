using Microsoft.EntityFrameworkCore;

namespace Nexus.Actions.Infrastructure;

/// <summary>
/// Brings an existing deployment's Actions schema up to the current model for: the link from an action to what raised it (SourceType /
/// SourceId), the approvers named on an action (Approver1UserId ... Approver3UserId), the optional pattern of a repeating action (days of the week, day of the month, months, hours per occurrence) and the table of
/// status-change requests. ModuleSchemaInitializer.EnsureCreatedAsync never adds a column or table to a schema that already exists, so they
/// have to be patched in. A host that runs EnsureCreatedAsync for this module can call this right after it on every start; it is a no-op
/// once everything exists. Hosts that manage the schema by script apply docs/upgrade/2026-10-14-add-action-status-and-patterns.sql, which
/// does the same thing in the same order.
/// </summary>
public static class ActionsPlanningSchemaUpgrade
{
    // Run one at a time (each is its own batch, as the GO-separated script is), in this order, in one transaction.
    // Kept identical to the DDL EF generates for the model; a test compares them. Every added column is nullable, so an
    // existing action simply has no source and no pattern.
    internal static readonly string[] Statements =
    [
        """
        IF COL_LENGTH(N'actions.Actions', N'SourceType') IS NULL
            ALTER TABLE [actions].[Actions] ADD [SourceType] nvarchar(40) NULL;
        """,

        """
        IF COL_LENGTH(N'actions.Actions', N'SourceId') IS NULL
            ALTER TABLE [actions].[Actions] ADD [SourceId] uniqueidentifier NULL;
        """,

        """
        IF COL_LENGTH(N'actions.Actions', N'RecurrenceDaysOfWeek') IS NULL
            ALTER TABLE [actions].[Actions] ADD [RecurrenceDaysOfWeek] int NULL;
        """,

        """
        IF COL_LENGTH(N'actions.Actions', N'RecurrenceDayOfMonth') IS NULL
            ALTER TABLE [actions].[Actions] ADD [RecurrenceDayOfMonth] int NULL;
        """,

        """
        IF COL_LENGTH(N'actions.Actions', N'RecurrenceMonths') IS NULL
            ALTER TABLE [actions].[Actions] ADD [RecurrenceMonths] int NULL;
        """,

        """
        IF COL_LENGTH(N'actions.Actions', N'RecurrenceDurationHours') IS NULL
            ALTER TABLE [actions].[Actions] ADD [RecurrenceDurationHours] decimal(5,2) NULL;
        """,

        """
        IF COL_LENGTH(N'actions.Actions', N'Approver1UserId') IS NULL
            ALTER TABLE [actions].[Actions] ADD [Approver1UserId] uniqueidentifier NULL;
        """,

        """
        IF COL_LENGTH(N'actions.Actions', N'Approver2UserId') IS NULL
            ALTER TABLE [actions].[Actions] ADD [Approver2UserId] uniqueidentifier NULL;
        """,

        """
        IF COL_LENGTH(N'actions.Actions', N'Approver3UserId') IS NULL
            ALTER TABLE [actions].[Actions] ADD [Approver3UserId] uniqueidentifier NULL;
        """,

        """
        IF OBJECT_ID(N'[actions].[ActionStatusSubmissions]', N'U') IS NULL
            CREATE TABLE [actions].[ActionStatusSubmissions] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [ProjectId] uniqueidentifier NULL,
                [ActionId] uniqueidentifier NOT NULL,
                [PreviousStatus] int NOT NULL,
                [SubmittedStatus] int NOT NULL,
                [Comment] nvarchar(2000) NULL,
                [ApprovalStatus] int NOT NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_ActionStatusSubmissions] PRIMARY KEY ([Id])
            );
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ActionStatusSubmissions_ActionId' AND object_id = OBJECT_ID(N'[actions].[ActionStatusSubmissions]'))
            CREATE INDEX [IX_ActionStatusSubmissions_ActionId] ON [actions].[ActionStatusSubmissions] ([ActionId]);
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ActionStatusSubmissions_ProjectId' AND object_id = OBJECT_ID(N'[actions].[ActionStatusSubmissions]'))
            CREATE INDEX [IX_ActionStatusSubmissions_ProjectId] ON [actions].[ActionStatusSubmissions] ([ProjectId]);
        """
    ];

    public static async Task EnsureCurrentAsync(DbContext actionsDbContext, CancellationToken cancellationToken)
    {
        await using var transaction = await actionsDbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var statement in Statements)
        {
            await actionsDbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
