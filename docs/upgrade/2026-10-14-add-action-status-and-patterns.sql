/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) for four Actions features:
      - an action remembers what it was raised from (a risk, a stakeholder ...): SourceType / SourceId;
      - an action records up to three people named to approve it: Approver1UserId ... Approver3UserId;
      - a repeating action can say which weekdays, which day of the Jalali month and which months it falls on, and how
        many hours each occurrence takes (its occurrences are listed by GET /api/actions/{id}/occurrences);
      - a status change of an action can be requested and confirmed by an approver (table ActionStatusSubmissions);
        until it is approved the action keeps its status.

    What it does, in order:
      1. adds nine NULLABLE columns to [actions].[Actions] (SourceType, SourceId, RecurrenceDaysOfWeek, RecurrenceDayOfMonth,
         RecurrenceMonths, RecurrenceDurationHours, Approver1UserId, Approver2UserId, Approver3UserId): every existing action
         has NULL in all of them, which means "no source, no pattern, no named approvers" - exactly how they have always behaved;
      2. creates [actions].[ActionStatusSubmissions] and its two lookup indexes.

    Nothing existing is changed or dropped. The new status OnHold (4) needs no schema change.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never adds a column or table to a
    schema that already exists. A host that prefers code can call
    Nexus.Actions.Infrastructure.ActionsPlanningSchemaUpgrade.EnsureCurrentAsync on startup instead; it runs the same
    statements in the same order. Safe to run more than once.

    Apply this BEFORE deploying the build that contains these changes: that build selects the columns, so an Actions table
    without them fails every query.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-14-add-action-status-and-patterns.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Skipped, without an error, on a database that does not have this module's tables yet: the module creates
-- its whole schema (this change included) the first time the host starts against such a database, and a
-- partial schema made here would stop it from doing so.
IF OBJECT_ID(N'[actions].[Actions]', N'U') IS NULL
BEGIN
    PRINT N'Skipped: Actions is not installed in this database ([actions].[Actions] does not exist). Nothing to upgrade.';
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;
GO

IF COL_LENGTH(N'actions.Actions', N'SourceType') IS NULL
    ALTER TABLE [actions].[Actions] ADD [SourceType] nvarchar(40) NULL;
GO

IF COL_LENGTH(N'actions.Actions', N'SourceId') IS NULL
    ALTER TABLE [actions].[Actions] ADD [SourceId] uniqueidentifier NULL;
GO

IF COL_LENGTH(N'actions.Actions', N'RecurrenceDaysOfWeek') IS NULL
    ALTER TABLE [actions].[Actions] ADD [RecurrenceDaysOfWeek] int NULL;
GO

IF COL_LENGTH(N'actions.Actions', N'RecurrenceDayOfMonth') IS NULL
    ALTER TABLE [actions].[Actions] ADD [RecurrenceDayOfMonth] int NULL;
GO

IF COL_LENGTH(N'actions.Actions', N'RecurrenceMonths') IS NULL
    ALTER TABLE [actions].[Actions] ADD [RecurrenceMonths] int NULL;
GO

IF COL_LENGTH(N'actions.Actions', N'RecurrenceDurationHours') IS NULL
    ALTER TABLE [actions].[Actions] ADD [RecurrenceDurationHours] decimal(5,2) NULL;
GO

IF COL_LENGTH(N'actions.Actions', N'Approver1UserId') IS NULL
    ALTER TABLE [actions].[Actions] ADD [Approver1UserId] uniqueidentifier NULL;
GO

IF COL_LENGTH(N'actions.Actions', N'Approver2UserId') IS NULL
    ALTER TABLE [actions].[Actions] ADD [Approver2UserId] uniqueidentifier NULL;
GO

IF COL_LENGTH(N'actions.Actions', N'Approver3UserId') IS NULL
    ALTER TABLE [actions].[Actions] ADD [Approver3UserId] uniqueidentifier NULL;
GO

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
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ActionStatusSubmissions_ActionId' AND object_id = OBJECT_ID(N'[actions].[ActionStatusSubmissions]'))
    CREATE INDEX [IX_ActionStatusSubmissions_ActionId] ON [actions].[ActionStatusSubmissions] ([ActionId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ActionStatusSubmissions_ProjectId' AND object_id = OBJECT_ID(N'[actions].[ActionStatusSubmissions]'))
    CREATE INDEX [IX_ActionStatusSubmissions_ProjectId] ON [actions].[ActionStatusSubmissions] ([ProjectId]);
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
