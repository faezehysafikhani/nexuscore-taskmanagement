-- Upgrade 21: Workflow (approvals) - task review, timesheet approval, approval chains, approver delegation.
-- Idempotent: safe to run again, also on a database where the API already created some of these tables.
-- The API creates the same tables at start-up when Database:SeedOnStartup is on; run this when the database
-- is managed by scripts instead. Generated from the WorkflowDbContext model, plus the columns later releases added.
IF SCHEMA_ID(N'workflow') IS NULL EXEC(N'CREATE SCHEMA [workflow];');
GO

IF OBJECT_ID(N'[workflow].[WorkflowDefinitions]', N'U') IS NULL
BEGIN
    CREATE TABLE [workflow].[WorkflowDefinitions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(160) NOT NULL,
        [SubjectType] nvarchar(80) NOT NULL,
        [ScopeType] nvarchar(80) NOT NULL,
        [ScopeId] uniqueidentifier NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_WorkflowDefinitions] PRIMARY KEY ([Id])
    );
END
GO

IF OBJECT_ID(N'[workflow].[WorkflowDelegations]', N'U') IS NULL
BEGIN
    CREATE TABLE [workflow].[WorkflowDelegations] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [DelegatorUserId] uniqueidentifier NOT NULL,
        [DelegateUserId] uniqueidentifier NOT NULL,
        [StartDate] date NOT NULL,
        [EndDate] date NOT NULL,
        [SubjectType] nvarchar(80) NULL,
        [Reason] nvarchar(500) NULL,
        [IsRevoked] bit NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_WorkflowDelegations] PRIMARY KEY ([Id])
    );
END
GO

IF OBJECT_ID(N'[workflow].[WorkflowInstances]', N'U') IS NULL
BEGIN
    CREATE TABLE [workflow].[WorkflowInstances] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [WorkflowDefinitionId] uniqueidentifier NOT NULL,
        [SubjectType] nvarchar(80) NOT NULL,
        [SubjectId] uniqueidentifier NOT NULL,
        [TotalSteps] int NOT NULL,
        [CurrentStepOrder] int NOT NULL,
        [Status] int NOT NULL,
        [StepsSnapshot] nvarchar(max) NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_WorkflowInstances] PRIMARY KEY ([Id])
    );
END
GO

IF OBJECT_ID(N'[workflow].[WorkflowSteps]', N'U') IS NULL
BEGIN
    CREATE TABLE [workflow].[WorkflowSteps] (
        [Id] uniqueidentifier NOT NULL,
        [WorkflowDefinitionId] uniqueidentifier NOT NULL,
        [Order] int NOT NULL,
        [Name] nvarchar(160) NOT NULL,
        [ApproverUserId] uniqueidentifier NULL,
        [ApproverRoleId] uniqueidentifier NULL,
        [ApproverKey] nvarchar(40) NULL,
        [IsOptional] bit NOT NULL,
        CONSTRAINT [PK_WorkflowSteps] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkflowSteps_WorkflowDefinitions_WorkflowDefinitionId] FOREIGN KEY ([WorkflowDefinitionId]) REFERENCES [workflow].[WorkflowDefinitions] ([Id]) ON DELETE CASCADE
    );
END
GO

IF OBJECT_ID(N'[workflow].[WorkflowDecisions]', N'U') IS NULL
BEGIN
    CREATE TABLE [workflow].[WorkflowDecisions] (
        [Id] uniqueidentifier NOT NULL,
        [WorkflowInstanceId] uniqueidentifier NOT NULL,
        [StepOrder] int NOT NULL,
        [DecidedByUserId] uniqueidentifier NOT NULL,
        [Approved] bit NOT NULL,
        [Comment] nvarchar(2000) NULL,
        [OnBehalfOfUserId] uniqueidentifier NULL,
        [DecidedAtUtc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_WorkflowDecisions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkflowDecisions_WorkflowInstances_WorkflowInstanceId] FOREIGN KEY ([WorkflowInstanceId]) REFERENCES [workflow].[WorkflowInstances] ([Id]) ON DELETE CASCADE
    );
END
GO

-- Columns added after the first Workflow release (a database created by an older API has the tables without them)
IF COL_LENGTH(N'workflow.WorkflowSteps', N'ApproverKey') IS NULL
    ALTER TABLE [workflow].[WorkflowSteps] ADD [ApproverKey] nvarchar(40) NULL;
GO
IF COL_LENGTH(N'workflow.WorkflowSteps', N'IsOptional') IS NULL
    ALTER TABLE [workflow].[WorkflowSteps] ADD [IsOptional] bit NOT NULL CONSTRAINT [DF_WorkflowSteps_IsOptional] DEFAULT CAST(0 AS bit);
GO
IF COL_LENGTH(N'workflow.WorkflowInstances', N'StepsSnapshot') IS NULL
    ALTER TABLE [workflow].[WorkflowInstances] ADD [StepsSnapshot] nvarchar(max) NULL;
GO
IF COL_LENGTH(N'workflow.WorkflowDecisions', N'OnBehalfOfUserId') IS NULL
    ALTER TABLE [workflow].[WorkflowDecisions] ADD [OnBehalfOfUserId] uniqueidentifier NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowDecisions_WorkflowInstanceId' AND object_id = OBJECT_ID(N'[workflow].[WorkflowDecisions]'))
    CREATE INDEX [IX_WorkflowDecisions_WorkflowInstanceId] ON [workflow].[WorkflowDecisions] ([WorkflowInstanceId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowDefinitions_TenantId_SubjectType_ScopeType_ScopeId_IsActive' AND object_id = OBJECT_ID(N'[workflow].[WorkflowDefinitions]'))
    CREATE INDEX [IX_WorkflowDefinitions_TenantId_SubjectType_ScopeType_ScopeId_IsActive] ON [workflow].[WorkflowDefinitions] ([TenantId], [SubjectType], [ScopeType], [ScopeId], [IsActive]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowDelegations_TenantId_DelegateUserId' AND object_id = OBJECT_ID(N'[workflow].[WorkflowDelegations]'))
    CREATE INDEX [IX_WorkflowDelegations_TenantId_DelegateUserId] ON [workflow].[WorkflowDelegations] ([TenantId], [DelegateUserId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowDelegations_TenantId_DelegatorUserId' AND object_id = OBJECT_ID(N'[workflow].[WorkflowDelegations]'))
    CREATE INDEX [IX_WorkflowDelegations_TenantId_DelegatorUserId] ON [workflow].[WorkflowDelegations] ([TenantId], [DelegatorUserId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowInstances_SubjectType_SubjectId' AND object_id = OBJECT_ID(N'[workflow].[WorkflowInstances]'))
    CREATE INDEX [IX_WorkflowInstances_SubjectType_SubjectId] ON [workflow].[WorkflowInstances] ([SubjectType], [SubjectId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowInstances_TenantId_Status' AND object_id = OBJECT_ID(N'[workflow].[WorkflowInstances]'))
    CREATE INDEX [IX_WorkflowInstances_TenantId_Status] ON [workflow].[WorkflowInstances] ([TenantId], [Status]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowSteps_WorkflowDefinitionId' AND object_id = OBJECT_ID(N'[workflow].[WorkflowSteps]'))
    CREATE INDEX [IX_WorkflowSteps_WorkflowDefinitionId] ON [workflow].[WorkflowSteps] ([WorkflowDefinitionId]);
GO
