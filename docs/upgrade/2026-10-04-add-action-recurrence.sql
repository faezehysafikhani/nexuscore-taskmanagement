/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) so an action can repeat:
    when a repeating action is completed, the next occurrence is created with its dates moved on by
    the interval (every N days / weeks / months / years), until an optional end date.

    Adds five NULLABLE columns to [actions].[Actions]:
      RecurrenceUnit       int   (0 Daily, 1 Weekly, 2 Monthly, 3 Yearly)
      RecurrenceInterval   int
      RecurrenceEndDate    date
      RecurrenceSourceId   uniqueidentifier   (the occurrence this one was created from)
      NextOccurrenceId     uniqueidentifier   (the occurrence created when this one was completed)

    Every existing action has NULL in all five, which means "a one-off action" - exactly how they
    have always behaved. Nothing is changed or dropped.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never adds
    a column to a table that already exists. A host that prefers code can call
    Nexus.Actions.Infrastructure.ActionsSchemaUpgrade.EnsureCurrentAsync on startup instead; it runs the
    same statements. Safe to run more than once.

    Apply this BEFORE deploying the build that contains action recurrence: that build selects the
    columns, so an Actions table without them fails every query.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-04-add-action-recurrence.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
GO

IF COL_LENGTH(N'actions.Actions', N'RecurrenceUnit') IS NULL
    ALTER TABLE [actions].[Actions] ADD [RecurrenceUnit] int NULL;
GO

IF COL_LENGTH(N'actions.Actions', N'RecurrenceInterval') IS NULL
    ALTER TABLE [actions].[Actions] ADD [RecurrenceInterval] int NULL;
GO

IF COL_LENGTH(N'actions.Actions', N'RecurrenceEndDate') IS NULL
    ALTER TABLE [actions].[Actions] ADD [RecurrenceEndDate] date NULL;
GO

IF COL_LENGTH(N'actions.Actions', N'RecurrenceSourceId') IS NULL
    ALTER TABLE [actions].[Actions] ADD [RecurrenceSourceId] uniqueidentifier NULL;
GO

IF COL_LENGTH(N'actions.Actions', N'NextOccurrenceId') IS NULL
    ALTER TABLE [actions].[Actions] ADD [NextOccurrenceId] uniqueidentifier NULL;
GO

COMMIT TRANSACTION;
GO
