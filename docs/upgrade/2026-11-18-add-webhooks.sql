/*
    Task management: webhook subscriptions and their delivery outbox (phase 9).
    Adds, never drops or rewrites. Safe to run more than once. Hosts that start with the task module applied
    (TaskManagementSchemaPatcher) get the same statements on startup.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-11-18-add-webhooks.sql
*/
SET NOCOUNT ON;
GO

IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[WebhookSubscriptions]') IS NULL
BEGIN
    CREATE TABLE [task_management].[WebhookSubscriptions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Url] nvarchar(500) NOT NULL,
        [Secret] nvarchar(100) NOT NULL,
        [Events] nvarchar(500) NOT NULL,
        [IsActive] bit NOT NULL,
        [ConsecutiveFailures] int NOT NULL,
        [LastDeliveredAtUtc] datetimeoffset NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [ModifiedAtUtc] datetimeoffset NULL,
        [ModifiedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_WebhookSubscriptions] PRIMARY KEY ([Id]));
    CREATE INDEX [IX_WebhookSubscriptions_TenantId_IsActive] ON [task_management].[WebhookSubscriptions] ([TenantId], [IsActive]);
END
IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[WebhookDeliveries]') IS NULL
BEGIN
    CREATE TABLE [task_management].[WebhookDeliveries] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [SubscriptionId] uniqueidentifier NOT NULL,
        [EventType] nvarchar(60) NOT NULL,
        [Payload] nvarchar(max) NOT NULL,
        [Attempts] int NOT NULL,
        [NextAttemptAtUtc] datetimeoffset NOT NULL,
        [DeliveredAtUtc] datetimeoffset NULL,
        [GaveUpAtUtc] datetimeoffset NULL,
        [LastStatusCode] int NULL,
        [LastError] nvarchar(500) NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_WebhookDeliveries] PRIMARY KEY ([Id]));
    CREATE INDEX [IX_WebhookDeliveries_DeliveredAtUtc_GaveUpAtUtc_NextAttemptAtUtc] ON [task_management].[WebhookDeliveries] ([DeliveredAtUtc], [GaveUpAtUtc], [NextAttemptAtUtc]);
    CREATE INDEX [IX_WebhookDeliveries_SubscriptionId_CreatedAtUtc] ON [task_management].[WebhookDeliveries] ([SubscriptionId], [CreatedAtUtc]);
END
GO
