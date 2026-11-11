/*
    Task management: how long the module keeps its own logs (phase 9).
    Adds, never drops or rewrites. Safe to run more than once. Hosts that start with the task module applied
    (TaskManagementSchemaPatcher) get the same statements on startup.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-11-20-add-retention.sql
*/
SET NOCOUNT ON;
GO

IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[TaskRetentionSettings]') IS NULL
    CREATE TABLE [task_management].[TaskRetentionSettings] (
        [TenantId] uniqueidentifier NOT NULL,
        [AutomationLogDays] int NULL,
        [WebhookDeliveryDays] int NULL,
        CONSTRAINT [PK_TaskRetentionSettings] PRIMARY KEY ([TenantId]));
GO
